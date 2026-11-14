using FluentValidation;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.DataLayer;
using PantryPress.Domain.Modules.Recipes.Validation;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Facades;

/// <inheritdoc cref="IRecipesFacade"/>
/// <remarks>
/// Every call works on the site the route's policy entered, and asks Sites for the caller's role there - the membership the
/// policy already read, cached for the request (auth.md) - so an Author's reach is decided by the same lookup that let
/// them in. Each id a call mints - a new recipe's, a new part's - is minted before its first attempt, so an attempt run
/// again after a commit that landed meets its own rows rather than adding others. Nothing here is audited or sent through
/// the outbox: editing a working copy changes nothing a reader sees (PP-D04).
/// </remarks>
internal sealed class RecipesFacade(
    IValidator<RecipeContent> contentValidator,
    IValidator<UpdateRecipeRequest> updateValidator,
    IValidator<ListRecipesQuery> listValidator,
    RecipesDataLayer data,
    ISiteMembershipFacade memberships,
    ISiteContext siteContext,
    IClock clock) : IRecipesFacade
{
    public async Task<Result<RecipeDetail>> CreateAsync(
        AccountId account,
        string siteSlug,
        RecipeContent content,
        CancellationToken cancellationToken)
    {
        if (await contentValidator.CheckAsync(content, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if ((await RoleAsync(account, siteSlug, cancellationToken)).Error is { } refused)
        {
            return refused;
        }

        // A new recipe has no parts yet, so any part id the caller names is not one of its parts: refused at the id, the
        // same answer a save gets, rather than taken as the new row's key (backend.md: the facade mints a new row's id).
        var edit = RecipeEdit.From(content, Guid.NewGuid);
        if (RecipeEditor.ClaimedParts(edit) is { Count: > 0 } claimed)
        {
            return Error.Validation(claimed);
        }

        var id = new RecipeId(Guid.NewGuid());
        var now = clock.UtcNow;
        var created = await data.RunLockedAsync(
            async token =>
            {
                if (await data.FindLockedAsync(id, token) is { } landed)
                {
                    return await data.ReadWorkingCopyAsync(landed, token);
                }

                var recipe = new Recipe { Id = id, CreatedAt = now, UpdatedAt = now, CreatedByAccountId = account };
                RecipesDataLayer.WriteNutrition(recipe, content.Nutrition);
                return StageNew(recipe, edit, now);
            },
            cancellationToken);

        return Detail(created, canEdit: true);
    }

    public async Task<Result<RecipeDetail>> GetAsync(
        AccountId account,
        string siteSlug,
        RecipeId recipe,
        CancellationToken cancellationToken)
    {
        var role = await RoleAsync(account, siteSlug, cancellationToken);
        if (!role.IsSuccess)
        {
            return role.Error;
        }

        // Read under the recipe's lock, so the parts are one save's, never half of one.
        var copy = await data.RunLockedAsync(
            async token => await data.FindLockedAsync(recipe, token) is { } found
                ? await data.ReadWorkingCopyAsync(found, token)
                : null,
            cancellationToken);
        return copy is null
            ? RecipeErrors.RecipeNotFound
            : Detail(copy, RecipeEditor.CanChange(role.Value, account, copy.Recipe.CreatedByAccountId));
    }

    public async Task<Result<RecipeSummaryPage>> ListAsync(
        AccountId account,
        string siteSlug,
        ListRecipesQuery query,
        CancellationToken cancellationToken)
    {
        if (await listValidator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var role = await RoleAsync(account, siteSlug, cancellationToken);
        if (!role.IsSuccess)
        {
            return role.Error;
        }

        RecipeListPosition? after = RecipeListCursor.TryRead(query.Cursor, out var position) ? position : null;
        var limit = query.Limit ?? RecipeListCursor.DefaultLimit;
        var rows = await data.ListPageAsync(query.Archived ?? false, after, limit + 1, cancellationToken);
        var more = rows.Count > limit;
        var page = rows.Take(limit).ToList();
        var next = more ? RecipeListCursor.After(new RecipeListPosition(page[^1].UpdatedAt, page[^1].Id)) : null;
        return new RecipeSummaryPage(
            [.. page.Select(r => RecipeDetails.Summary(r, RecipeEditor.CanChange(role.Value, account, r.CreatedByAccountId)))],
            next);
    }

    public async Task<Result<RecipeDetail>> UpdateAsync(
        AccountId account,
        string siteSlug,
        RecipeId recipe,
        UpdateRecipeRequest request,
        CancellationToken cancellationToken)
    {
        if (await updateValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var role = await RoleAsync(account, siteSlug, cancellationToken);
        if (!role.IsSuccess)
        {
            return role.Error;
        }

        RecipeVersion.TryRead(request.Version, out var expected);
        var content = request.Content!;
        var edit = RecipeEdit.From(content, Guid.NewGuid);
        var now = clock.UtcNow;

        // Decided under the recipe's lock, which holds until the commit: nothing read here can change before what is staged
        // is saved. A refusal or an unchanged recipe stages nothing, and saves nothing.
        var saved = await data.RunLockedAsync<Result<RecipeWorkingCopy>>(
            async token =>
            {
                if (await data.FindLockedAsync(recipe, token) is not { } found)
                {
                    return RecipeErrors.RecipeNotFound;
                }

                if (!RecipeEditor.CanChange(role.Value, account, found.CreatedByAccountId))
                {
                    return RecipeErrors.RecipeNotYours;
                }

                if (found.ArchivedAt is not null)
                {
                    return RecipeErrors.RecipeArchived;
                }

                var current = await data.ReadWorkingCopyAsync(found, token);
                if (RecipeEditor.UnknownParts(current, edit, "content.") is { Count: > 0 } unknown)
                {
                    return Error.Validation(unknown);
                }

                var before = RecipeSnapshotJson.Write(current.ToSnapshot()).Hash;
                var editedAt = found.UpdatedAt;
                RecipesDataLayer.WriteNutrition(found, content.Nutrition);
                var plan = RecipeEditor.Apply(current, edit, RecipesDataLayer.NutritionHundredths(found), now);

                // The same recipe, word for word and part for part: nothing to save, whatever version was sent, so a retry
                // after a lost answer - or an autosave with nothing new - hears the recipe as it is.
                if (RecipeRevisionRules.IsRepeat(before, RecipeSnapshotJson.Write(plan.After.ToSnapshot()).Hash))
                {
                    found.UpdatedAt = editedAt;
                    return plan.After;
                }

                if (!RecipeRevisionRules.IsCurrent(expected, found.Version))
                {
                    return RecipeErrors.RecipeChanged;
                }

                data.StageChangedRecipe(found);
                data.StageParts(plan);
                return plan.After;
            },
            cancellationToken);

        return saved.IsSuccess ? Detail(saved.Value, canEdit: true) : saved.Error;
    }

    public Task<Result> ArchiveAsync(AccountId account, string siteSlug, RecipeId recipe, CancellationToken cancellationToken) =>
        SetArchivedAsync(account, siteSlug, recipe, archive: true, cancellationToken);

    public Task<Result> RestoreAsync(AccountId account, string siteSlug, RecipeId recipe, CancellationToken cancellationToken) =>
        SetArchivedAsync(account, siteSlug, recipe, archive: false, cancellationToken);

    public async Task<Result<RecipeDetail>> DuplicateAsync(
        AccountId account,
        string siteSlug,
        RecipeId recipe,
        CancellationToken cancellationToken)
    {
        if ((await RoleAsync(account, siteSlug, cancellationToken)).Error is { } refused)
        {
            return refused;
        }

        var id = new RecipeId(Guid.NewGuid());
        var now = clock.UtcNow;
        var copied = await data.RunLockedAsync<Result<RecipeWorkingCopy>>(
            async token =>
            {
                if (await data.FindLockedAsync(id, token) is { } landed)
                {
                    return await data.ReadWorkingCopyAsync(landed, token);
                }

                if (await data.FindLockedAsync(recipe, token) is not { } source)
                {
                    return RecipeErrors.RecipeNotFound;
                }

                var original = await data.ReadWorkingCopyAsync(source, token);
                var copy = new Recipe { Id = id, CreatedAt = now, UpdatedAt = now, CreatedByAccountId = account };
                RecipesDataLayer.WriteNutrition(copy, RecipesDataLayer.ReadNutrition(source));
                return StageNew(copy, RecipeEdit.Copy(original, RecipeEditor.CopyTitle(source.Title), Guid.NewGuid), now);
            },
            cancellationToken);

        return copied.IsSuccess ? Detail(copied.Value, canEdit: true) : copied.Error;
    }

    private async Task<Result> SetArchivedAsync(
        AccountId account,
        string siteSlug,
        RecipeId recipe,
        bool archive,
        CancellationToken cancellationToken)
    {
        var role = await RoleAsync(account, siteSlug, cancellationToken);
        if (!role.IsSuccess)
        {
            return role.Error;
        }

        var now = clock.UtcNow;
        return await data.RunLockedAsync<Result>(
            async token =>
            {
                if (await data.FindLockedAsync(recipe, token) is not { } found)
                {
                    return RecipeErrors.RecipeNotFound;
                }

                if (!RecipeEditor.CanChange(role.Value, account, found.CreatedByAccountId))
                {
                    return RecipeErrors.RecipeNotYours;
                }

                // Already where it was asked to be: nothing changes, so a retry hears the same answer. Archiving is not an
                // edit of the recipe, so its place in the library (UpdatedAt) stays where it was.
                if ((found.ArchivedAt is not null) == archive)
                {
                    return Result.Success;
                }

                found.ArchivedAt = archive ? now : null;
                data.StageChangedRecipe(found);
                return Result.Success;
            },
            cancellationToken);
    }

    // A new recipe and every part of it, staged in the attempt running now.
    private RecipeWorkingCopy StageNew(Recipe recipe, RecipeEdit edit, DateTimeOffset now)
    {
        var plan = RecipeEditor.Apply(
            RecipeWorkingCopy.New(recipe, RecipesDataLayer.NutritionHundredths(recipe)),
            edit,
            RecipesDataLayer.NutritionHundredths(recipe),
            now);
        data.StageNewRecipe(recipe);
        data.StageParts(plan);
        return plan.After;
    }

    // Built once the attempt has committed, so the version is the one the save gave the recipe's row.
    private static RecipeDetail Detail(RecipeWorkingCopy copy, bool canEdit) =>
        RecipeDetails.From(copy, RecipesDataLayer.ReadNutrition(copy.Recipe), canEdit);

    // The caller's role on the site the route's policy entered. The policy found it moments ago in this request, and the
    // membership lookup is cached for the request, so this asks the database nothing new.
    private async Task<Result<SiteRole>> RoleAsync(AccountId account, string siteSlug, CancellationToken cancellationToken)
    {
        if (await memberships.FindAsync(account, siteSlug, cancellationToken) is not { } membership)
        {
            return SiteErrors.SiteNotFound;
        }

        if (membership.SiteId != siteContext.Required)
        {
            throw new InvalidOperationException("The request's site is not the one its route names.");
        }

        return membership.Role;
    }
}

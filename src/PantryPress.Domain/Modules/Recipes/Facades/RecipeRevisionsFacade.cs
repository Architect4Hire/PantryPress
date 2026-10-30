using FluentValidation;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.DataLayer;

namespace PantryPress.Domain.Modules.Recipes.Facades;

/// <inheritdoc cref="IRecipeRevisionsFacade"/>
internal sealed class RecipeRevisionsFacade(
    IValidator<CreateRecipeRevisionRequest> validator,
    RecipeRevisionsDataLayer data,
    IClock clock) : IRecipeRevisionsFacade
{
    public async Task<Result<RecipeRevisionCreated>> CreateRevisionAsync(
        CreateRecipeRevisionRequest request,
        CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        // Minted once, before any attempt, so a retry of a commit that landed meets its own revision rather than adding
        // another.
        var id = new RecipeRevisionId(Guid.NewGuid());
        var now = clock.UtcNow;
        var (recipeId, kind) = (request.RecipeId, request.Kind);

        // Each attempt decides under the recipe's lock, which holds until the commit, so nothing it read can change before
        // what it staged is saved. A refusal or a repeat stages nothing, and saves nothing.
        return await data.RunLockedAsync<Result<RecipeRevisionCreated>>(
            async token =>
            {
                if (await data.FindLockedAsync(recipeId, token) is not { } recipe)
                {
                    return RecipeErrors.RecipeNotFound;
                }

                if (await data.FindNumberAsync(id, token) is { } landed)
                {
                    return new RecipeRevisionCreated(id, landed, kind, Created: true);
                }

                if (!RecipeRevisionRules.IsCurrent(request.ExpectedVersion, recipe.Version))
                {
                    return RecipeErrors.RecipeChanged;
                }

                var document = RecipeSnapshotJson.Write((await data.ReadWorkingCopyAsync(recipe, token)).ToSnapshot());
                if (await data.FindLatestAsync(recipeId, kind, token) is { } latest
                    && RecipeRevisionRules.IsRepeat(latest.ContentHash, document.Hash))
                {
                    return new RecipeRevisionCreated(latest.Id, latest.Number, kind, Created: false);
                }

                var number = RecipeRevisionRules.NextNumber(await data.FindLatestNumberAsync(recipeId, token));
                data.StageRevision(RecipeRevisionRules.NewRevision(id, recipeId, number, kind, document, now));
                return new RecipeRevisionCreated(id, number, kind, Created: true);
            },
            cancellationToken);
    }
}

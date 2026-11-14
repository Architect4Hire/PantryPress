using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Repositories;

/// <summary>
/// A recipe and its parts, the working copy (PP4.3, PP4.4). Every read goes through the site filter, so a recipe of
/// another site is no recipe here.
/// </summary>
internal sealed class RecipeRepository(PantryPressDbContext db, ISiteContext siteContext)
{
    /// <summary>
    /// How a library search compares a title (PP4.12a): ignoring case and accents, so "creme" finds "Crème", and the same in
    /// every database whatever its default collation.
    /// </summary>
    public const string SearchCollation = "Latin1_General_100_CI_AI";

    /// <summary>The escape character of <see cref="ContainsPattern"/>.</summary>
    public const string LikeEscape = @"\";

    /// <summary>
    /// A LIKE pattern that finds <paramref name="text"/> anywhere, as typed: every LIKE wildcard in it (<c>%</c>, <c>_</c>,
    /// <c>[</c>) and the escape itself are escaped with <see cref="LikeEscape"/>, so a search for <c>50%</c> or <c>[new]</c>
    /// finds exactly those characters.
    /// </summary>
    public static string ContainsPattern(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var escaped = new System.Text.StringBuilder(text.Length + 8).Append('%');
        foreach (var c in text)
        {
            if (c is '\\' or '%' or '_' or '[')
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.Append('%').ToString();
    }

    /// <summary>
    /// The recipe, untracked, with its row held under an update lock until the transaction ends, or null. Every change to
    /// a recipe or any part of it moves this row in the same transaction (PP4.11), so while the lock is held no edit can
    /// commit, and what is read after it is one whole recipe, never half of an edit. The raw query names the request's
    /// site as well as the id, so the lock is taken by a seek on the key, and the filter then applies over it as usual.
    /// An update lock alone is held to the transaction's end; no range lock is taken (no <c>HOLDLOCK</c>), because a miss
    /// would then lock the gap up to the next key - which can be another site's recipe - and stall that site's edits.
    /// </summary>
    public async Task<Recipe?> FindLockedAsync(RecipeId id, CancellationToken cancellationToken)
    {
        var site = siteContext.Required.Value;
        var found = await db.Set<Recipe>()
            .FromSql($"SELECT * FROM [Recipes] WITH (UPDLOCK, ROWLOCK) WHERE [SiteId] = {site} AND [Id] = {id.Value}")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return found.SingleOrDefault();
    }

    /// <summary>Every part of <paramref name="recipe"/>, untracked, read inside the transaction that holds its lock.</summary>
    public async Task<RecipeWorkingCopy> ReadWorkingCopyAsync(Recipe recipe, CancellationToken cancellationToken)
    {
        var id = recipe.Id;
        var ingredientGroups = await db.Set<RecipeIngredientGroup>().AsNoTracking().Where(g => g.RecipeId == id).ToListAsync(cancellationToken);
        var ingredients = await db.Set<RecipeIngredient>()
            .AsNoTracking()
            .Where(i => db.Set<RecipeIngredientGroup>().Any(g => g.RecipeId == id && g.Id == i.GroupId))
            .ToListAsync(cancellationToken);
        var stepGroups = await db.Set<RecipeStepGroup>().AsNoTracking().Where(g => g.RecipeId == id).ToListAsync(cancellationToken);
        var steps = await db.Set<RecipeStep>()
            .AsNoTracking()
            .Where(s => db.Set<RecipeStepGroup>().Any(g => g.RecipeId == id && g.Id == s.GroupId))
            .ToListAsync(cancellationToken);

        return new RecipeWorkingCopy(
            recipe,
            Nutrition(recipe),
            ingredientGroups,
            ingredients,
            stepGroups,
            steps,
            await db.Set<RecipeEquipment>().AsNoTracking().Where(e => e.RecipeId == id).ToListAsync(cancellationToken),
            await db.Set<RecipeNote>().AsNoTracking().Where(n => n.RecipeId == id).ToListAsync(cancellationToken),
            await db.Set<RecipeKeyword>().AsNoTracking().Where(k => k.RecipeId == id).ToListAsync(cancellationToken),
            await db.Set<RecipeCourse>().AsNoTracking().Where(c => c.RecipeId == id).ToListAsync(cancellationToken),
            await db.Set<RecipeCuisine>().AsNoTracking().Where(c => c.RecipeId == id).ToListAsync(cancellationToken),
            await db.Set<RecipeDiet>().AsNoTracking().Where(d => d.RecipeId == id).ToListAsync(cancellationToken),
            await db.Set<RecipeAllergen>().AsNoTracking().Where(a => a.RecipeId == id).ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Up to <paramref name="take"/> of the site's recipes that <paramref name="filter"/> holds, most recently edited first,
    /// after <paramref name="after"/> (PP4.11, PP4.12a). The keyset comparison is SQL Server's own uniqueidentifier order, the
    /// same order the list is sorted in, so a page after a cursor never repeats a recipe that has not been edited since. The
    /// raw query names the site as well, and the filter applies over it as usual.
    /// <para>
    /// The search is a LIKE over the title under an explicit case- and accent-insensitive collation, so what it finds never
    /// depends on the database's default; its wildcards are escaped. Each code is an <c>EXISTS</c> on its join table, keyed
    /// by the site filter and correlated on the recipe's own site and id, so it is a seek on the join table's key
    /// <c>(SiteId, RecipeId, Code)</c>; the <c>(SiteId, Code)</c> index serves the optimizer if it drives from the join.
    /// </para>
    /// </summary>
    public Task<List<Recipe>> ListPageAsync(
        RecipeListFilter filter,
        RecipeListPosition? after,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var archived = filter.Archived;
        var site = siteContext.Required.Value;
        var recipes = after is { } position
            ? db.Set<Recipe>().FromSql($"""
                SELECT * FROM [Recipes]
                WHERE [SiteId] = {site}
                  AND ([UpdatedAt] < {position.UpdatedAt}
                    OR ([UpdatedAt] = {position.UpdatedAt} AND [Id] < {position.Recipe.Value}))
                """)
            : db.Set<Recipe>();
        var query = recipes
            .AsNoTracking()
            .Where(r => archived ? r.ArchivedAt != null : r.ArchivedAt == null);
        if (filter.Search is { } search)
        {
            var pattern = ContainsPattern(search);
            query = query.Where(r => EF.Functions.Like(EF.Functions.Collate(r.Title, SearchCollation), pattern, LikeEscape));
        }

        if (filter.Course is { } course)
        {
            query = query.Where(r => db.Set<RecipeCourse>().Any(c => c.SiteId == r.SiteId && c.RecipeId == r.Id && c.CourseCode == course));
        }

        if (filter.Cuisine is { } cuisine)
        {
            query = query.Where(r => db.Set<RecipeCuisine>().Any(c => c.SiteId == r.SiteId && c.RecipeId == r.Id && c.CuisineCode == cuisine));
        }

        if (filter.Diet is { } diet)
        {
            query = query.Where(r => db.Set<RecipeDiet>().Any(d => d.SiteId == r.SiteId && d.RecipeId == r.Id && d.DietCode == diet));
        }

        return query
            .OrderByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Stages <paramref name="row"/> - the recipe or one of its parts, untracked - as added, changed, or removed, saved when
    /// the attempt it was staged in commits. A changed or removed row keeps the site and version it was read with, so the
    /// stamp and the concurrency token still decide whether the save may touch it (tenancy.md rule 3).
    /// </summary>
    public void StageAdded(object row) => db.Add(Owned(row));

    /// <inheritdoc cref="StageAdded"/>
    public void StageChanged(object row) => db.Update(Owned(row));

    /// <inheritdoc cref="StageAdded"/>
    public void StageRemoved(object row) => db.Remove(Owned(row));

    // Only the working copy's rows are staged here; a revision is added by its own repository alone, never past it.
    private static object Owned(object row) =>
        row is Recipe or RecipeIngredientGroup or RecipeIngredient or RecipeStepGroup or RecipeStep or RecipeEquipment
            or RecipeNote or RecipeKeyword or RecipeCourse or RecipeCuisine or RecipeDiet or RecipeAllergen
            ? row
            : throw new InvalidOperationException($"{row.GetType().Name} is not a row of a recipe's working copy.");

    private static RecipeSnapshotNutrition Nutrition(Recipe recipe) => RecipeNutritionColumns.Hundredths(recipe);
}

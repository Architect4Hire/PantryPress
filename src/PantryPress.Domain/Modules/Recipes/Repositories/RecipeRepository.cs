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

    // Nutrition in hundredths, which is what its two-place columns hold (RCP-006). Here, beside the columns, because the
    // Recipes Business code never names a fractional number type (PP-D05).
    private static RecipeSnapshotNutrition Nutrition(Recipe recipe) => new(
        recipe.ServingSize,
        Hundredths(recipe.Calories),
        Hundredths(recipe.FatGrams),
        Hundredths(recipe.SaturatedFatGrams),
        Hundredths(recipe.TransFatGrams),
        Hundredths(recipe.UnsaturatedFatGrams),
        Hundredths(recipe.CholesterolMilligrams),
        Hundredths(recipe.SodiumMilligrams),
        Hundredths(recipe.CarbohydrateGrams),
        Hundredths(recipe.FiberGrams),
        Hundredths(recipe.SugarGrams),
        Hundredths(recipe.ProteinGrams));

    // Exact, never rounded: a value with more places than the column keeps is not one the column could have held.
    private static long? Hundredths(decimal? value) => value switch
    {
        null => null,
        { Scale: <= RecipeLimits.NutritionScale } v => decimal.ToInt64(v * 100m),
        { } v => throw new InvalidOperationException(
            $"A nutrition value has {v.Scale} decimal places; its column keeps {RecipeLimits.NutritionScale}."),
    };
}

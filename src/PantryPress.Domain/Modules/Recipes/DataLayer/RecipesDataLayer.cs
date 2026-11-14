using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.Repositories;

namespace PantryPress.Domain.Modules.Recipes.DataLayer;

/// <summary>
/// Composes the recipe repository for the recipe routes (ARCH-002; PP4.11): the reads the facade decides by, the rows a save
/// stages, and the one transaction they run in. Every read of one recipe is made under its lock, inside
/// <see cref="RunLockedAsync"/>, so a recipe is always read whole and a save is decided on what it will overwrite.
/// </summary>
internal sealed class RecipesDataLayer(RecipeRepository recipes, RecipesUnitOfWork unitOfWork)
{
    /// <inheritdoc cref="RecipesUnitOfWork.RunLockedAsync"/>
    public Task<T> RunLockedAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken cancellationToken) =>
        unitOfWork.RunLockedAsync(attempt, cancellationToken);

    /// <inheritdoc cref="RecipeRepository.FindLockedAsync"/>
    public Task<Recipe?> FindLockedAsync(RecipeId recipe, CancellationToken cancellationToken) =>
        recipes.FindLockedAsync(recipe, cancellationToken);

    /// <inheritdoc cref="RecipeRepository.ReadWorkingCopyAsync"/>
    public Task<RecipeWorkingCopy> ReadWorkingCopyAsync(Recipe recipe, CancellationToken cancellationToken) =>
        recipes.ReadWorkingCopyAsync(recipe, cancellationToken);

    /// <inheritdoc cref="RecipeRepository.ListPageAsync"/>
    public Task<List<Recipe>> ListPageAsync(bool archived, RecipeListPosition? after, int take, CancellationToken cancellationToken) =>
        recipes.ListPageAsync(archived, after, take, cancellationToken);

    /// <summary>Stages a new recipe's row.</summary>
    public void StageNewRecipe(Recipe recipe) => recipes.StageAdded(recipe);

    /// <summary>
    /// Stages a change to the recipe's own row, which moves its version: every save of a recipe or any part of it does, so
    /// the one token guards the whole recipe (PP4.3, PP4.4).
    /// </summary>
    public void StageChangedRecipe(Recipe recipe) => recipes.StageChanged(recipe);

    /// <summary>Stages every row <paramref name="plan"/> adds, changes, and removes.</summary>
    public void StageParts(RecipeEditPlan plan)
    {
        foreach (var row in plan.Added)
        {
            recipes.StageAdded(row);
        }

        foreach (var row in plan.Updated)
        {
            recipes.StageChanged(row);
        }

        foreach (var row in plan.Removed)
        {
            recipes.StageRemoved(row);
        }
    }

    /// <inheritdoc cref="RecipeNutritionColumns.Hundredths"/>
    public static RecipeSnapshotNutrition NutritionHundredths(Recipe recipe) => RecipeNutritionColumns.Hundredths(recipe);

    /// <inheritdoc cref="RecipeNutritionColumns.Read"/>
    public static RecipeNutrition? ReadNutrition(Recipe recipe) => RecipeNutritionColumns.Read(recipe);

    /// <inheritdoc cref="RecipeNutritionColumns.Write"/>
    public static void WriteNutrition(Recipe recipe, RecipeNutrition? nutrition) => RecipeNutritionColumns.Write(recipe, nutrition);
}

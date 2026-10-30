using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.Repositories;

namespace PantryPress.Domain.Modules.Recipes.DataLayer;

/// <summary>
/// Composes the recipe and revision repositories for freezing a recipe into a revision (ARCH-002; PP4.4): the reads the
/// facade decides by, the row it stages, and the one transaction they run in. The reads are made under the recipe's lock,
/// inside <see cref="RunLockedAsync"/>.
/// </summary>
internal sealed class RecipeRevisionsDataLayer(
    RecipeRepository recipes,
    RecipeRevisionRepository revisions,
    RecipesUnitOfWork unitOfWork)
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

    /// <inheritdoc cref="RecipeRevisionRepository.FindNumberAsync"/>
    public Task<int?> FindNumberAsync(RecipeRevisionId revision, CancellationToken cancellationToken) =>
        revisions.FindNumberAsync(revision, cancellationToken);

    /// <inheritdoc cref="RecipeRevisionRepository.FindLatestNumberAsync"/>
    public Task<int?> FindLatestNumberAsync(RecipeId recipe, CancellationToken cancellationToken) =>
        revisions.FindLatestNumberAsync(recipe, cancellationToken);

    /// <inheritdoc cref="RecipeRevisionRepository.FindLatestAsync"/>
    public Task<RecipeRevisionHead?> FindLatestAsync(RecipeId recipe, RecipeRevisionKind kind, CancellationToken cancellationToken) =>
        revisions.FindLatestAsync(recipe, kind, cancellationToken);

    /// <summary>Stages a new revision, saved when the attempt it was staged in commits.</summary>
    public void StageRevision(RecipeRevision revision) => revisions.Add(revision);
}

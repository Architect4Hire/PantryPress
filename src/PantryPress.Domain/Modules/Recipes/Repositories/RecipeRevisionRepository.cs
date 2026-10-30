using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Repositories;

/// <summary>
/// A recipe's revisions (PP4.4), through the site filter. Revisions are only ever added: the context refuses a change or
/// removal of one, and the table's trigger refuses it in the database. Each read here is made while the recipe's lock is
/// held, which every freeze of the same recipe takes first, so a number or a latest revision read here cannot be overtaken
/// before the save.
/// </summary>
internal sealed class RecipeRevisionRepository(PantryPressDbContext db)
{
    /// <summary>The number of the revision with this id, if it was saved.</summary>
    public Task<int?> FindNumberAsync(RecipeRevisionId id, CancellationToken cancellationToken) =>
        db.Set<RecipeRevision>().Where(r => r.Id == id).Select(r => (int?)r.Number).SingleOrDefaultAsync(cancellationToken);

    /// <summary>The recipe's highest revision number of either kind, or null for a recipe with none.</summary>
    public Task<int?> FindLatestNumberAsync(RecipeId recipe, CancellationToken cancellationToken) =>
        db.Set<RecipeRevision>().Where(r => r.RecipeId == recipe).MaxAsync(r => (int?)r.Number, cancellationToken);

    /// <summary>The recipe's latest revision of <paramref name="kind"/>, or null.</summary>
    public Task<RecipeRevisionHead?> FindLatestAsync(RecipeId recipe, RecipeRevisionKind kind, CancellationToken cancellationToken) =>
        db.Set<RecipeRevision>()
            .Where(r => r.RecipeId == recipe && r.Kind == kind)
            .OrderByDescending(r => r.Number)
            .Select(r => new RecipeRevisionHead(r.Id, r.Number, r.ContentHash))
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(RecipeRevision revision) => db.Add(revision);
}

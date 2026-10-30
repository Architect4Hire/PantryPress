using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// Freezes a recipe as it is being edited into a revision (RCP-007; PP-D04; PP4.4). The recipe tables are the working copy,
/// the draft a blogger edits; a revision is a copy of it at one moment, written once and never changed or removed, so
/// editing a published recipe changes only the working copy and never what readers were shown. Works on the request's
/// site: the caller has entered it, and a recipe of any other site is no recipe here.
/// </summary>
/// <remarks>
/// No host registers this yet. Who may freeze a revision, and what publishing asks first - an author's review (POST-010),
/// a confirmed email address (ACCT-001) - are the routes' and the publish use case's to decide (PP4.11, PP5.14).
/// </remarks>
public interface IRecipeRevisionsFacade
{
    /// <summary>
    /// Freezes the recipe as a revision of <see cref="CreateRecipeRevisionRequest.Kind"/>, if it is still the version the
    /// caller read. Success carries the revision: a new one, or - when the latest revision of that kind already holds
    /// exactly this recipe - that one, unchanged, so repeating a call adds nothing. Otherwise
    /// <see cref="RecipeErrors.RecipeNotFound"/>, <see cref="RecipeErrors.RecipeChanged"/> (nothing saved), or a
    /// validation error.
    /// </summary>
    Task<Result<RecipeRevisionCreated>> CreateRevisionAsync(CreateRecipeRevisionRequest request, CancellationToken cancellationToken);
}

/// <summary>Freeze a recipe into a revision.</summary>
/// <param name="RecipeId">The recipe, on the request's site.</param>
/// <param name="Kind">A draft save point, or what readers will see.</param>
/// <param name="ExpectedVersion">
/// The recipe's version as the caller read it (its row version, 8 bytes). A recipe changed since is refused, never frozen
/// with edits the caller has not seen.
/// </param>
public sealed record CreateRecipeRevisionRequest(RecipeId RecipeId, RecipeRevisionKind Kind, byte[]? ExpectedVersion);

/// <summary>The revision a freeze ended with.</summary>
/// <param name="Id">The revision's id.</param>
/// <param name="Number">Its number among the recipe's revisions of either kind, from 1.</param>
/// <param name="Kind">Draft or published.</param>
/// <param name="Created">False when the latest revision of this kind already held exactly this recipe, and is returned.</param>
public sealed record RecipeRevisionCreated(RecipeRevisionId Id, int Number, RecipeRevisionKind Kind, bool Created);

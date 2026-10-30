using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// A recipe frozen at one moment (RCP-007; PP-D04; PP4.4): every word and part of it as one canonical snapshot document,
/// written once and never changed or removed (<see cref="IAppendOnly"/>). A draft revision is a save point; a published one
/// is what readers see, and what page data, JSON-LD, and a post's recipe card are built from (ARCH-004). Editing the recipe
/// afterwards changes only the working copy, the recipe tables. Site-owned and private (PP-D06), under its recipe by
/// <c>(SiteId, RecipeId)</c>.
/// <para>
/// The snapshot is never rewritten, so every format version ever written stays readable for good
/// (<see cref="SnapshotVersion"/>). Its hash is of the snapshot's exact bytes: two revisions with the same hash hold the
/// same recipe.
/// </para>
/// </summary>
public sealed class RecipeRevision : ISiteOwned, IAppendOnly
{
    public RecipeRevisionId Id { get; init; }

    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public RecipeId RecipeId { get; init; }

    /// <summary>Its place among the recipe's revisions of either kind, from 1, in the order they were made.</summary>
    public int Number { get; init; }

    public RecipeRevisionKind Kind { get; init; }

    /// <summary>Which format <see cref="Snapshot"/> is written in.</summary>
    public int SnapshotVersion { get; init; }

    /// <summary>The recipe as canonical JSON, the same bytes for the same recipe.</summary>
    public string Snapshot { get; init; } = string.Empty;

    /// <summary>SHA-256 of <see cref="Snapshot"/>'s UTF-8 bytes.</summary>
    public byte[] ContentHash { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
}

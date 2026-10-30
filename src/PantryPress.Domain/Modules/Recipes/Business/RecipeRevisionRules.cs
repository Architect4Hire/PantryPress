using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// The rules of freezing a recipe into a revision (RCP-007; PP4.4), which the facade applies under the recipe's lock.
/// </summary>
internal static class RecipeRevisionRules
{
    /// <summary>A recipe's version is its row version: eight bytes the database moves on every update.</summary>
    public const int VersionLength = 8;

    /// <summary>
    /// Whether the recipe is still the one the caller read. Anything else is refused, never frozen with edits the caller
    /// has not seen (Template D: no silent last-write-wins).
    /// </summary>
    public static bool IsCurrent(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) =>
        expected.Length == VersionLength && expected.SequenceEqual(actual);

    /// <summary>
    /// Whether freezing again would only repeat the latest revision of the same kind: the same bytes, so the same recipe.
    /// A repeat adds nothing, so a retried publish is safe. A draft and a published revision of the same recipe are two
    /// revisions: each kind is compared only with its own.
    /// </summary>
    public static bool IsRepeat(ReadOnlySpan<byte> latestOfKind, ReadOnlySpan<byte> hash) =>
        !latestOfKind.IsEmpty && latestOfKind.SequenceEqual(hash);

    /// <summary>The next revision's number, counting both kinds, from 1.</summary>
    public static int NextNumber(int? latest) => checked((latest ?? 0) + 1);

    /// <summary>The revision that freezes <paramref name="document"/> as <paramref name="kind"/>, under its number.</summary>
    public static RecipeRevision NewRevision(
        RecipeRevisionId id,
        RecipeId recipe,
        int number,
        RecipeRevisionKind kind,
        RecipeSnapshotDocument document,
        DateTimeOffset now) => new()
        {
            Id = id,
            RecipeId = recipe,
            Number = number,
            Kind = kind,
            SnapshotVersion = RecipeSnapshotJson.CurrentVersion,
            Snapshot = document.Json,
            ContentHash = document.Hash,
            CreatedAt = now,
        };
}

/// <summary>The latest revision of a kind, as much of it as deciding the next one needs.</summary>
internal sealed record RecipeRevisionHead(RecipeRevisionId Id, int Number, byte[] ContentHash);

using System.Buffers.Binary;
using System.Buffers.Text;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>Where a page of recipes ends: the last recipe's edit time and id, in the list's order.</summary>
public readonly record struct RecipeListPosition(DateTimeOffset UpdatedAt, RecipeId Recipe);

/// <summary>
/// The cursor of a site's recipe list (api-contract.md: cursor-based pagination; PP4.11): the last recipe of a page, by edit
/// time and then id, both descending, opaque to the client so its shape can change. A recipe edited while the list is being
/// paged moves to the top, so it may be missed or shown twice; nothing else moves. It names a position, never a site: a
/// cursor from another site's list only positions this one.
/// </summary>
public static class RecipeListCursor
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

    // Eight bytes of UTC ticks, big-endian, then the recipe's sixteen.
    private const int Length = sizeof(long) + 16;

    /// <summary>The cursor that continues after <paramref name="position"/>.</summary>
    public static string After(RecipeListPosition position)
    {
        Span<byte> bytes = stackalloc byte[Length];
        BinaryPrimitives.WriteInt64BigEndian(bytes, position.UpdatedAt.UtcTicks);
        if (!position.Recipe.Value.TryWriteBytes(bytes[sizeof(long)..]))
        {
            throw new InvalidOperationException("A recipe id is sixteen bytes.");
        }

        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// The position a cursor continues after, when it is one this made: exactly the bytes <see cref="After"/> writes, in its
    /// one spelling. Anything else is not a cursor.
    /// </summary>
    public static bool TryRead(string? cursor, out RecipeListPosition position)
    {
        position = default;
        if (cursor is not { Length: > 0 and <= 64 } || !Base64Url.IsValid(cursor, out var decodedLength) || decodedLength != Length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[Length];
        if (!Base64Url.TryDecodeFromChars(cursor, bytes, out var written) || written != Length)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        var read = new RecipeListPosition(new DateTimeOffset(ticks, TimeSpan.Zero), new RecipeId(new Guid(bytes[sizeof(long)..])));
        if (!string.Equals(After(read), cursor, StringComparison.Ordinal))
        {
            return false;
        }

        position = read;
        return true;
    }
}

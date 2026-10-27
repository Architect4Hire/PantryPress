using System.Buffers.Binary;
using System.Buffers.Text;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>Where a page of a site's members ends: the last member's join time and account, in the list's order.</summary>
public readonly record struct SiteMemberPosition(DateTimeOffset JoinedAt, AccountId Account);

/// <summary>
/// The cursor of a site's member list (api-contract.md: cursor-based pagination; PP2.11b): the last member of a page, by
/// join time and then account, opaque to the client so its shape can change. A member's join time and account never
/// change, so a page after it neither repeats nor skips anyone still there. It names a position, never a site: a cursor
/// from another site's list only positions this one.
/// </summary>
public static class SiteMemberCursor
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

    // Eight bytes of UTC ticks, big-endian, then the account's sixteen.
    private const int Length = sizeof(long) + 16;

    /// <summary>The cursor that continues after <paramref name="position"/>.</summary>
    public static string After(SiteMemberPosition position)
    {
        Span<byte> bytes = stackalloc byte[Length];
        BinaryPrimitives.WriteInt64BigEndian(bytes, position.JoinedAt.UtcTicks);
        if (!position.Account.Value.TryWriteBytes(bytes[sizeof(long)..]))
        {
            throw new InvalidOperationException("An account id is sixteen bytes.");
        }

        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// The position a cursor continues after, when it is one this made: exactly the bytes <see cref="After"/> writes, in
    /// its one spelling. Anything else is not a cursor.
    /// </summary>
    public static bool TryRead(string? cursor, out SiteMemberPosition position)
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

        var read = new SiteMemberPosition(new DateTimeOffset(ticks, TimeSpan.Zero), new AccountId(new Guid(bytes[sizeof(long)..])));
        if (!string.Equals(After(read), cursor, StringComparison.Ordinal))
        {
            return false;
        }

        position = read;
        return true;
    }
}

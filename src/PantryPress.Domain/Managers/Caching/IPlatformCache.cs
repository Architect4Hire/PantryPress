using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Caching;

/// <summary>
/// What a read of a <see cref="IPlatformCache"/> entry found: its value, if one is cached, and the entry's version, which
/// a write must still match. Pass it back to <see cref="IPlatformCache.WriteAsync"/> unchanged.
/// </summary>
/// <param name="Value">The cached value, or null when nothing is cached.</param>
/// <param name="Version">How many times the entry has been invalidated; zero when it never was, or that has expired.</param>
public readonly record struct CacheRead(string? Value, long Version);

/// <summary>
/// The cache for what belongs to no site (tenancy.md rule 8; PP2.12): entries under a <see cref="PlatformCacheKey"/>, and
/// nothing else, so no entry is ever filed under a key someone built by hand. The first user is the public tier's host map;
/// site entries under a <see cref="SiteCacheKey"/> arrive with page data (PP7.12).
/// </summary>
/// <remarks>
/// <para>
/// An entry is filled from the database after a miss, so a read that started before a write and finishes after it
/// would put the old answer back. Every entry therefore carries a version: <see cref="InvalidateAsync"/> moves it, and
/// <see cref="WriteAsync"/> writes only while it is still the one the read saw. A lookup that raced a change is simply
/// not cached; the next one reads the database again.
/// </para>
/// <para>
/// The cache is never worth waiting for. When it cannot be reached every call fails at once with
/// <see cref="CacheUnavailableException"/>, and the caller carries on without it. No call takes a cancellation token:
/// each is one round trip that is either answered or refused immediately.
/// </para>
/// </remarks>
public interface IPlatformCache
{
    /// <summary>The entry's value and version.</summary>
    Task<CacheRead> ReadAsync(PlatformCacheKey key);

    /// <summary>
    /// Caches <paramref name="value"/> for <paramref name="lifetime"/>, unless the entry was invalidated since
    /// <paramref name="seen"/> was read. True when it was written.
    /// </summary>
    Task<bool> WriteAsync(PlatformCacheKey key, CacheRead seen, string value, TimeSpan lifetime);

    /// <summary>
    /// Drops the entry's value and moves its version, so no read made before this call can write its answer back.
    /// </summary>
    Task InvalidateAsync(PlatformCacheKey key);
}

/// <summary>The cache could not be reached, or did not answer in time. Whoever called it carries on without it.</summary>
public sealed class CacheUnavailableException(string message, Exception innerException) : Exception(message, innerException);

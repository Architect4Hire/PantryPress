namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// Why a slug is on the reserved list (SITE-002; PP2.8). Stored as these numbers, so they never change; a new reason takes
/// the next one.
/// </summary>
public enum ReservedSlugReason
{
    /// <summary>
    /// A host PantryPress itself answers on, or has DNS records under: <c>app</c> above all, whose <c>__Host-</c> cookies a
    /// tenant site on that host would share an origin with (PSEC-003, PP1.12).
    /// </summary>
    PlatformHost = 0,

    /// <summary>A name the internet's plumbing treats as special: mail, name servers, discovery, and the like.</summary>
    Infrastructure = 1,

    /// <summary>A name a reader could take for PantryPress speaking: support, billing, sign-in, and the like.</summary>
    Impersonation = 2,

    /// <summary>A name PantryPress may want for a page of its own.</summary>
    Product = 3,
}

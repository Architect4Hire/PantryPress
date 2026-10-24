namespace PantryPress.Domain.Modules.Accounts;

/// <summary>
/// A signed-out BFF session (ACCT-002, PSEC-003; PP1.12). The gateway's ticket is a cookie and nothing server-side, so
/// signing out on its own only cleared the browser's copy: a copied cookie kept working until it expired or the
/// account's security stamp moved. The gateway now gives each session an id, and signing out records it here, so the
/// next request with that cookie is refused wherever it comes from. Account data, not site data: no <c>SiteId</c>
/// (PP-D06). Kept only as long as a session could still be alive, then purged by the Worker.
/// </summary>
public sealed class RevokedSession
{
    /// <summary>The gateway's random session id: 32 bytes, base64url. Unguessable, so it is the key on its own.</summary>
    public string SessionId { get; set; } = string.Empty;

    public Guid AccountId { get; set; }

    public DateTimeOffset RevokedAt { get; set; }

    /// <summary>When no ticket that carries this id can still be alive, so the row has nothing left to refuse.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

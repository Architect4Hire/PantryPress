using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// An invitation to join a site by email, with the role the invited person will have (SITE-010; PP2.11). Site-owned and
/// private (PP-D06): the global filter and the save's stamp keep it to its site, so an Owner's request reads and writes
/// only its own site's invitations, and accepting reads one only after entering the site its link names. It holds a
/// person's email address, so nothing about it is logged, audited, or put in the outbox but its id.
/// <para>
/// It works once and for a while: open while <see cref="Status"/> is <see cref="SiteInvitationStatus.Pending"/> and the
/// clock is before <see cref="ExpiresAt"/>. Accepting moves it to <see cref="SiteInvitationStatus.Accepted"/>, guarded by
/// <see cref="Version"/>, so of two accepts at once one wins; inviting the same address again revokes it.
/// </para>
/// </summary>
public sealed class SiteInvitation : ISiteOwned
{
    public SiteInvitationId Id { get; init; }

    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    /// <summary>The address as the Owner typed it: where the email goes.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>The address in the form two spellings of it compare in (<c>EmailAddresses.Normalize</c>).</summary>
    public string NormalizedEmail { get; init; } = string.Empty;

    public SiteRole Role { get; init; }

    public SiteInvitationStatus Status { get; set; }

    public AccountId InvitedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The account that joined with it, once accepted.</summary>
    public AccountId? AcceptedBy { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>The concurrency token: the database moves it on every update, and a save from a stale read fails.</summary>
    public byte[] Version { get; set; } = [];
}

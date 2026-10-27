using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// The Sites module's audited actions (PP1.11, OPS-006), each recorded with the change it describes and the site it
/// happened on. The codes are stable: the operator console reads them.
/// </summary>
public static class SiteAudit
{
    /// <summary>A site was created, with its subdomain and its first Owner (PP2.10).</summary>
    public const string Created = "site.created";

    /// <summary>An Owner invited someone to the site by email (PP2.11). The invitation holds the address; the entry does not.</summary>
    public const string MemberInvited = "site.member-invited";

    /// <summary>An account joined the site through an invitation (PP2.11).</summary>
    public const string MemberJoined = "site.member-joined";

    /// <summary>The target kind for a site.</summary>
    public const string TargetKind = "site";

    /// <summary>The target kind for an invitation to a site.</summary>
    public const string InvitationTargetKind = "site-invitation";

    public static AuditTarget Target(SiteId site) => new(TargetKind, site.ToString());

    public static AuditTarget Target(SiteInvitationId invitation) => new(InvitationTargetKind, invitation.ToString());

    /// <summary>An account creating a site.</summary>
    public static AuditEvent SiteCreated(AccountId account, SiteId site) =>
        new(Created, AuditActor.Account(account.Value), Target(site));

    /// <summary>An Owner sending an invitation.</summary>
    public static AuditEvent Invited(AccountId owner, SiteInvitationId invitation) =>
        new(MemberInvited, AuditActor.Account(owner.Value), Target(invitation));

    /// <summary>An account accepting an invitation; the invitation records the role it joined with.</summary>
    public static AuditEvent Joined(AccountId account, SiteInvitationId invitation) =>
        new(MemberJoined, AuditActor.Account(account.Value), Target(invitation));
}

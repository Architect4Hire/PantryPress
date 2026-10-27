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

    /// <summary>The target kind for a site.</summary>
    public const string TargetKind = "site";

    public static AuditTarget Target(SiteId site) => new(TargetKind, site.ToString());

    /// <summary>An account creating a site.</summary>
    public static AuditEvent SiteCreated(AccountId account, SiteId site) =>
        new(Created, AuditActor.Account(account.Value), Target(site));
}

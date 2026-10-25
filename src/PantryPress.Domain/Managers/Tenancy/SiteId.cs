namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// The id of a site, which is the tenant (PP-D03; backend.md: strongly typed ids). Every site-owned row carries one, and
/// other modules refer to a site only by this. It is the shared kernel's (PP2.4), because the site context, the query
/// filter, the outbox, and the audit log all speak it; the Sites module owns the site it names.
/// </summary>
public readonly record struct SiteId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

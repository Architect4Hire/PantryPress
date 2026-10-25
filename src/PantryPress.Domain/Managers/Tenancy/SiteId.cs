namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>
/// The id of a site, which is the tenant (PP-D03; backend.md: strongly typed ids). Every site-owned row carries one, and
/// other modules refer to a site only by this.
/// </summary>
public readonly record struct SiteId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

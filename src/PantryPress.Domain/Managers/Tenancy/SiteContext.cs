namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>The scoped <see cref="ISiteContext"/> and its <see cref="ISiteContextWriter"/>: one per request or job.</summary>
internal sealed class SiteContext : ISiteContext, ISiteContextWriter
{
    public SiteId? Current { get; private set; }

    public SiteId Required => Current
        ?? throw new InvalidOperationException("This work needs a site, and no site was resolved for it.");

    public void Enter(SiteId site)
    {
        if (site.Value == Guid.Empty)
        {
            throw new ArgumentException("A site context needs a site's id.", nameof(site));
        }

        if (Current is { } current && current != site)
        {
            throw new InvalidOperationException("This scope already works on another site; one scope works on one site.");
        }

        Current = site;
    }
}

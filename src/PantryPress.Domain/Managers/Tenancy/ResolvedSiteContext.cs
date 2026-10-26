namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// Admits only the kernel's own scoped site context (PP2.5), the one a resolver enters through
/// <see cref="ISiteContextWriter"/>. Anything else implementing <see cref="ISiteContext"/> - a stand-in made by hand, or a
/// host's own registration ahead of the kernel's - would choose a site no membership or host resolution admitted.
/// </summary>
internal static class ResolvedSiteContext
{
    public static ISiteContext Require(ISiteContext context) => context is SiteContext
        ? context
        : throw new InvalidOperationException(
            $"A site-scoped context reads only the site a resolver entered; {context.GetType().Name} is not that context. Register it with AddSiteContext.");
}

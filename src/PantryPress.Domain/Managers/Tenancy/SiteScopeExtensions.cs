namespace PantryPress.Domain.Managers.Tenancy;

internal static class SiteScopeExtensions
{
    /// <summary>The site a kernel writer files under for <paramref name="scope"/>: the context's, or none.</summary>
    public static SiteId? Resolve(this ISiteContext context, SiteScope scope) => scope switch
    {
        SiteScope.Current => context.Required,
        SiteScope.None => null,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Not a site scope."),
    };
}

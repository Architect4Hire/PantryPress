using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Site.Hosting;

namespace PantryPress.Tests.Site;

/// <summary>What the probe route saw: the resolved public site and the request's site context.</summary>
internal sealed record SiteProbeAnswer(Guid SiteId, string Host, string CanonicalHost, bool IsCanonical, string Status, Guid? ContextSite);

/// <summary>
/// A test-only route behind the public tier's whole pipeline (PP2.12): it answers what host resolution decided, so a test
/// can see which site a Host reached. The public tier has no endpoint of its own until PP2.13. Added after the host's own
/// pipeline, so it runs only where an unmatched request would otherwise get the 404, and only once resolution let the
/// request through.
/// </summary>
internal sealed class SiteProbes : IStartupFilter
{
    public const string Path = "/api/public/v1/test-probe/site";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use(async (context, inner) =>
        {
            if (!context.Request.Path.Equals(Path, StringComparison.Ordinal))
            {
                await inner(context);
                return;
            }

            var site = context.RequestServices.GetRequiredService<IPublicSiteContext>().Site;
            var current = context.RequestServices.GetRequiredService<ISiteContext>().Current;
            await context.Response.WriteAsJsonAsync(new SiteProbeAnswer(
                site.SiteId.Value,
                site.Host,
                site.CanonicalHost,
                site.IsCanonical,
                site.Status.ToString(),
                current?.Value));
        });
    };
}

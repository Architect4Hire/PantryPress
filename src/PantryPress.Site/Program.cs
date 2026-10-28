using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Sites;
using PantryPress.ServiceDefaults.Caching;
using PantryPress.ServiceDefaults.EdgeHardening;
using PantryPress.Site.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

// The anonymous public tier (ARCH-003, PP2.12). Nobody signs in here: no Identity, authentication, antiforgery, session,
// or cookie of any kind is registered, and a test keeps it so. The database is read only to fill the host map on a miss;
// the context refuses to build without the pinned Identity schema even in a host that never touches an account (PP1.1).
builder.Services.AddAccountsIdentitySchema();
builder.Services.AddPantryPressDbContext((services, options) => options.UseSqlServer(
    services.GetRequiredService<IConfiguration>().GetConnectionString("pantrypress")
        ?? throw new InvalidOperationException("The public site has no pantrypress connection string.")));
builder.EnrichSqlServerDbContext<PantryPressDbContext>();

// A request's site, from its Host header alone, through the host map in Redis (SITE-005, PSEC-004): once the map holds a
// host, answering for it makes no SQL query.
builder.AddCacheConnection();
builder.Services.AddSitePublicResolution();
builder.Services.AddPublicSiteContext();

// Problem details for every refusal, with the public tier's codes and plain titles.
builder.Services.AddSiteProblems();

// The public endpoints (PP2.13): controllers only, every one under the public surface and behind the status answers
// (SITE-008, SEO-007).
builder.Services.AddPublicControllers();
builder.Services.AddSiteOpenApi();

// The edge (PSEC-002; PP1.8). The public site is the one service tenant browsers reach, so it claims HTTPS for a
// year outside Development; the content-security policy here is a strict placeholder until it is generated per site
// from the blogger's settings, which is where an ad or analytics origin earns its place (MON-005, PP9).
builder.Services.AddTrustedProxies(
    builder.Configuration.GetSection("Edge:TrustedProxies"),
    required: !builder.Environment.IsDevelopment());

// Here the host is the tenant (PSEC-004; PP2.12): a proxy that may say what host was asked for may say whose site a
// reader sees, so no configuration can grant it, however a deployment copies the gateway's settings - and this stands
// for good. Behind Front Door the reader's host arrives in X-PP-Reader-Host instead, which host resolution reads only with
// the edge secret, or the SSR server's internal service token (PP9.3, PP7.13; owner decision, PP2.14).
builder.Services.AddOptions<TrustedProxyOptions>()
    .Validate(
        o => !o.ForwardHost && o.AllowedHosts.Count == 0,
        "Edge:TrustedProxies:ForwardHost must stay off on the public site: the host chooses the site, and no proxy may set "
        + "it through X-Forwarded-Host.")
    .ValidateOnStart();

// Nor may the framework's host filter run here: an unlisted host would get its bodiless 400 ahead of resolution, where
// every host that names no site must get the neutral 404 (PP2.14).
builder.Services.AddOptions<HostFilteringOptions>()
    .Validate(
        o => o.AllowedHosts.Count == 0 || o.AllowedHosts.Contains("*"),
        "AllowedHosts must stay '*' on the public site: a host that names no site gets the neutral 404, never a bare 400.")
    .ValidateOnStart();
builder.Services.AddRequestLimits(RequestLimits.PublicDefault);
builder.Services.AddSecurityHeaders(options =>
{
    options.Hsts = !builder.Environment.IsDevelopment();

    // Not on a blogger's own domain: DOM-001 allows an apex, and a year of includeSubDomains from example.com pins
    // hosts PantryPress neither serves nor was given permission to speak for.
    options.HstsIncludeSubDomains = false;
    options.ReferrerPolicy = ReferrerPolicies.Public;
    options.PermissionsPolicy = PermissionsPolicies.Public;
    options.DocumentContentSecurityPolicy = ContentSecurityPolicies.Site;
});

var app = builder.Build();
app.UseSiteProblems();
app.UseSecurityHeaders();

// A peer named in Edge:TrustedProxies is believed about the reader's address and scheme, never about the host, and every
// other forwarding header comes off; a site is resolved from the Host header alone until PP9.3 adds the edge-proven
// X-PP-Reader-Host (PSEC-004; PP2.14).
app.UseTrustedProxies();
app.UseForwardingHeaderHygiene();
app.UseRequestLimits();

app.MapDefaultEndpoints();

// After the strip, so the Host the connection carried is the only one read; a Host that names no active site ends here
// with the neutral 404 an unknown path gets.
app.UseSiteHostResolution();

// The OpenAPI document, in Development only, and on a resolved site's host like everything else.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

// MVC builds its action descriptors lazily, on the first request that needs them, and PublicSurfaceConvention runs with
// them. Touching them here makes a route outside the public surface fail the start instead of the first reader.
_ = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors;
app.EnsureControllersOnly();

app.Run();

/// <summary>
/// Names this assembly for the in-memory test host. <c>Program</c> is implicit and sits in the global namespace,
/// which every service's host shares, so the tests need a type of their own to point at.
/// </summary>
public sealed class SiteEntryPoint;

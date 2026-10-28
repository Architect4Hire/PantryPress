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

// The edge (PSEC-002; PP1.8). The public site is the one service tenant browsers reach, so it claims HTTPS for a
// year outside Development; the content-security policy here is a strict placeholder until it is generated per site
// from the blogger's settings, which is where an ad or analytics origin earns its place (MON-005, PP9).
builder.Services.AddTrustedProxies(
    builder.Configuration.GetSection("Edge:TrustedProxies"),
    required: !builder.Environment.IsDevelopment());

// Here the host is the tenant (PSEC-004; PP2.12): a proxy that may say what host was asked for may say whose site a
// reader sees, so no configuration can grant it, however a deployment copies the gateway's settings.
builder.Services.AddOptions<TrustedProxyOptions>()
    .Validate(
        o => !o.ForwardHost && o.AllowedHosts.Count == 0,
        "Edge:TrustedProxies:ForwardHost must stay off on the public site: the Host header chooses the site, and only the "
        + "connection may set it.")
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

// Nothing is trusted yet: a site is resolved from the Host header alone (PSEC-004), and proving that a request came
// through Front Door rather than straight at the origin is PP9.3's. Until then the strip is the whole rule, so no
// caller can pass itself off as a proxy.
app.UseTrustedProxies();
app.UseForwardingHeaderHygiene();
app.UseRequestLimits();

app.MapDefaultEndpoints();

// After the strip, so the Host the connection carried is the only one read; a Host that names no active site ends here
// with the neutral 404 an unknown path gets.
app.UseSiteHostResolution();

app.Run();

/// <summary>
/// Names this assembly for the in-memory test host. <c>Program</c> is implicit and sits in the global namespace,
/// which every service's host shares, so the tests need a type of their own to point at.
/// </summary>
public sealed class SiteEntryPoint;

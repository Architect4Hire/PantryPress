using PantryPress.Domain.Managers.Time;
using PantryPress.ServiceDefaults.EdgeHardening;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

// Problem details so a refusal has a body at all. The public tier's own vocabulary - codes, titles, and the shapes
// readers see - arrives with its first endpoints (PP2, PP7).
builder.Services.AddProblemDetails();

// The edge (PSEC-002; PP1.8). The public site is the one service tenant browsers reach, so it claims HTTPS for a
// year outside Development; the content-security policy here is a strict placeholder until it is generated per site
// from the blogger's settings, which is where an ad or analytics origin earns its place (MON-005, PP9).
builder.Services.AddTrustedProxies(
    builder.Configuration.GetSection("Edge:TrustedProxies"),
    required: !builder.Environment.IsDevelopment());
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
app.UseSecurityHeaders();

// Nothing is trusted yet: a site is resolved from the Host header alone (PSEC-004), and proving that a request came
// through Front Door rather than straight at the origin is PP9.3's. Until then the strip is the whole rule, so no
// caller can pass itself off as a proxy.
app.UseTrustedProxies();
app.UseForwardingHeaderHygiene();
app.UseRequestLimits();

app.MapDefaultEndpoints();

app.Run();

/// <summary>
/// Names this assembly for the in-memory test host. <c>Program</c> is implicit and sits in the global namespace,
/// which every service's host shares, so the tests need a type of their own to point at.
/// </summary>
public sealed class SiteEntryPoint;

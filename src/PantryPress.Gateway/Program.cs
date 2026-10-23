using System.Text.Json;
using System.Text.Json.Serialization;
using PantryPress.Gateway.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Routes and clusters are declared in configuration (gateway.md). Destinations name Aspire resources
// (http://apiservice, http://admin) and resolve through service discovery, so no service address is written down. The
// admin API route ships in every environment; Development adds a catch-all route to the admin dev server, and
// production has no SPA route yet (PP19 serves the built files).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms(EdgeHeaders.AddForwardedTransforms)
    .AddTransforms(SessionIdentity.AddSessionTransforms);

// Identity forwarding, header hygiene, and the session's response handling all key on this route id, and cookie
// stripping would fail silently if it were renamed in configuration. So the host says so at startup instead.
if (!builder.Configuration.GetSection($"ReverseProxy:Routes:{GatewayRoutes.AdminApi}").Exists())
{
    throw new InvalidOperationException(
        $"No YARP route '{GatewayRoutes.AdminApi}' is configured. The admin API route carries the session identity "
        + "the API is told about (PP1.6), so the gateway will not start without it.");
}

builder.Services.AddApiProblems();
builder.Services.AddApiReachabilityCheck();

// The edge (PSEC-002; PP1.8): who may say where a request came from, how much body it may carry, the headers every
// answer leaves with, and the redirect that says the admin app is HTTPS. HSTS outside Development only, so no
// developer's browser is pinned by a local run.
builder.Services.AddTrustedProxies(
    builder.Configuration.GetSection("Edge:TrustedProxies"),
    required: !builder.Environment.IsDevelopment());

// The gateway answers on hosts of its own, so a trusted proxy may say which one it was - the same-origin and
// antiforgery checks read it - but only from that list, never a host of the proxy's invention. The public tier
// leaves this off entirely, because there the host chooses the tenant.
builder.Services.Configure<TrustedProxyOptions>(options =>
{
    options.ForwardHost = true;
    foreach (var host in builder.Configuration.GetSection("Gateway:Hosts").Get<string[]>() ?? [])
    {
        options.AllowedHosts.Add(host);
    }
});
builder.Services.AddRequestLimits(RequestLimits.AdminDefault);
builder.Services.AddEdgeHttpsRedirect();
builder.Services.AddSecurityHeaders(options =>
{
    options.Hsts = !builder.Environment.IsDevelopment();
    options.ReferrerPolicy = ReferrerPolicies.Admin;
    options.PermissionsPolicy = PermissionsPolicies.DenyAll;
    options.DocumentContentSecurityPolicy = builder.Environment.IsDevelopment()
        ? ContentSecurityPolicies.DevelopmentDocument
        : ContentSecurityPolicies.Document;
});

// The browser boundary: nothing cross-origin, and a token on every write (ACCT-002; PP1.7).
builder.Services.AddClosedCors(builder.Configuration.GetSection("Gateway"));
builder.Services.AddGatewayAntiforgery();
builder.Services.AddGatewayRateLimits(builder.Configuration.GetSection("Gateway"));

// The BFF session: this gateway holds it, the browser never holds a token (ACCT-002, PSEC-003, PP-D13).
builder.Services.AddSessionCookies();
ApiSessions.AddApiSessions(builder.Services, builder.Configuration.GetSection("Gateway"));

// Enums are kebab-case strings on every PantryPress API surface (api-contract.md).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false)));

var app = builder.Build();

// Every failure the gateway produces gets the API's problem shape: an unhandled fault here, and YARP's bodiless 502 or
// 504 when a forwarded request never reached ApiService.
app.UseExceptionHandler();
app.UseStatusCodePages();

// The headers every answer carries, registered first so the ones that never reach an endpoint - a 413, a
// cross-origin refusal, a problem 404 - carry them too (PSEC-002).
app.UseSecurityHeaders();

// A configured proxy's word, then nobody else's: the trusted-proxy step reads the forwarding headers a known peer
// set and corrects the address, scheme, and host, and hygiene takes what is left off every inbound request, on every
// path, so nothing downstream - routing, the session, the proxy's own transforms - sees a client's invention. This
// order is what makes the checks below work behind Front Door, where TLS ends at the edge.
app.UseTrustedProxies();
app.UseForwardingHeaderHygiene();

// Addressed to a host that is ours, now that the host is the browser's rather than the hop's.
app.UseKnownHosts();

// Now that the scheme is the browser's, plain HTTP can be sent where it belongs. Probes are exempt.
app.UseEdgeHttpsRedirect();

// Before any body is read, by this gateway or the destination it would have been streamed to.
app.UseRequestLimits();

// Then the browser boundary, in the order an attack meets it: a preflight is answered by closed CORS, a
// cross-origin write is turned away on its Origin, and what is left needs the antiforgery token. All three sit ahead
// of endpoint execution, so no endpoint can be written that forgets them.
app.UseCors();
app.UseSameOriginWrites();
app.UseGatewayAntiforgery();
app.UseRateLimiter();

// Deliberately after those checks, and called here rather than left to the host: a WebApplication adds authentication
// ahead of every middleware of its own accord, which would bind each antiforgery token to whoever was signed in when
// it was minted - and refuse the token the admin app is holding the moment a sign-in, a second step, or a sign-out
// moved the session underneath it (PP1.7). Nothing downstream is affected: the session routes and the proxy read the
// session themselves.
app.UseAuthentication();

app.MapDefaultEndpoints();
app.MapApiFallback();
app.MapSessionEndpoints();

// The identity the API is allowed to believe is added inside the proxy pipeline, where the matched route is known and
// a half-finished sign-in can be refused instead of forwarded. The rest of the pipeline is YARP's own default.
app.MapReverseProxy(proxy =>
{
    proxy.UseSessionIdentity();
    proxy.UseSessionAffinity();
    proxy.UseLoadBalancing();
    proxy.UsePassiveHealthChecks();
});

app.Run();

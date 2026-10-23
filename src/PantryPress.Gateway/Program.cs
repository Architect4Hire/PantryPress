using System.Text.Json;
using System.Text.Json.Serialization;
using PantryPress.Gateway.Hosting;

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

// The browser boundary: nothing cross-origin, and a token on every write (ACCT-002; PP1.7).
builder.Services.AddClosedCors();
builder.Services.AddGatewayAntiforgery();

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

// Header hygiene first, so nothing downstream - routing, the session, the proxy's own transforms - ever sees a
// client's claim about the hops in front of us. Then the browser boundary, in the order an attack meets it: a
// preflight is answered by closed CORS, a cross-origin write is turned away on its Origin, and what is left needs
// the antiforgery token. All three sit ahead of routing, so no endpoint can be written that forgets them.
app.UseForwardingHeaderHygiene();
app.UseCors();
app.UseSameOriginWrites();
app.UseGatewayAntiforgery();

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

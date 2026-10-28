using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Sites;
using PantryPress.ServiceDefaults.Caching;
using PantryPress.ServiceDefaults.EdgeHardening;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

// The database: connection string from the pantrypress reference, and the Aspire client's retries, tracing, and a
// readiness check. One context per request, never pooled, because its site filter and save read the request's site
// (PP2.5). Accounts pins the Identity schema the migrations were built with (PP1.1).
builder.Services.AddPantryPressDbContext((services, options) => options.UseSqlServer(
    services.GetRequiredService<IConfiguration>().GetConnectionString("pantrypress")
        ?? throw new InvalidOperationException("ApiService has no pantrypress connection string.")));
builder.EnrichSqlServerDbContext<PantryPressDbContext>();

// Link tokens and authenticator keys are protected with this key ring. Locally it is the developer's profile; PP19
// moves it to storage protected by Key Vault, which must exist before any real account enables two-step sign-in.
builder.Services.AddDataProtection().SetApplicationName("PantryPress");

// Facades stage side effects in the transactional outbox, which commits with their own save; the Worker delivers them
// (PP1.10). The API only writes: it never registers the dispatcher, and it sends no email of its own - account email is
// staged there too and sent by the Worker (PP1.10b), so this host needs no email gateway and no link origin.
builder.Services.AddOutbox();

// Creating a site refreshes the public tier's host map in Redis, so the new address answers at once (PP2.12). The API
// carries on without Redis: the map's entries expire on their own.
builder.AddCacheConnection();

builder.Services.AddAccounts();
builder.Services.AddSites();
builder.Services.AddApiControllers();
builder.Services.AddGatewayTrust(builder.Configuration.GetSection("Gateway"));
builder.Services.AddApiAuthentication();
builder.Services.AddApiRateLimits(builder.Configuration.GetSection("RateLimits"));
builder.Services.AddApiIdempotency(builder.Configuration.GetSection("Idempotency"));
builder.Services.AddApiOpenApi();

// The edge, for a service a browser never reaches directly (PSEC-002; PP1.8): the same response headers, a body
// ceiling well under Kestrel's 30 MB default, and no HSTS or HTTPS redirect - the gateway and the SSR server call
// this service over plain HTTP inside the environment, so a redirect here would break the system, not harden it.
builder.Services.AddRequestLimits(RequestLimits.AdminDefault);
builder.Services.AddSecurityHeaders(options =>
{
    options.Hsts = false;
    options.ReferrerPolicy = ReferrerPolicies.Admin;
    options.PermissionsPolicy = PermissionsPolicies.DenyAll;
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();

// Who the client is, from the gateway alone, before anything partitions a rate limit by address.
app.UseGatewayForwardedClient();
app.UseRequestLimits();
app.UseNoStoreByDefault();
app.MapDefaultEndpoints();

// The internal surface is the gateway's alone (PP1.6): anyone else is answered "not here" before anything counts them.
app.UseInternalSurfaceGuard();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// A retried write gets its first answer back (PP1.9). After the rate limiter, so a replay still counts against it, and
// after authorization, because a key belongs to the caller that was let in.
app.UseIdempotency();

// The OpenAPI documents and the reference UI for reading them, in Development only (PP1.5).
if (app.Environment.IsDevelopment())
{
    app.MapApiReference();
}

app.MapControllers();

// MVC builds its action descriptors lazily, on the first request that needs them, and ApiSurfaceConvention runs with
// them. Touching them here makes a route outside a declared API surface fail the start instead of the first caller.
_ = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors;

app.Run();

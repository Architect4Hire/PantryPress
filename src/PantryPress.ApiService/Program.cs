using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.ServiceDefaults.EdgeHardening;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

// The database through the Aspire client: connection string from the pantrypress reference, retries, tracing, and a
// readiness check. Accounts pins the Identity schema the migrations were built with (PP1.1).
builder.AddSqlServerDbContext<PantryPressDbContext>("pantrypress");

// Link tokens and authenticator keys are protected with this key ring. Locally it is the developer's profile; PP19
// moves it to storage protected by Key Vault, which must exist before any real account enables two-step sign-in.
builder.Services.AddDataProtection().SetApplicationName("PantryPress");

builder.Services.AddAccounts(builder.Configuration.GetSection("Accounts"));
builder.AddApiEmail();
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

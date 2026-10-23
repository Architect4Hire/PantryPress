using Microsoft.AspNetCore.DataProtection;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;

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
builder.Services.AddApiAuthentication();
builder.Services.AddApiRateLimits(builder.Configuration.GetSection("RateLimits"));
builder.Services.AddApiOpenApi();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseNoStoreByDefault();
app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// The OpenAPI document for the admin API, in Development only; PP1.5 adds the reference UI and publishing it.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

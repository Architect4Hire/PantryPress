using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PantryPress.Domain.Integrations.DevelopmentMail;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Worker.Outbox;

// A web host only so the orchestrator can probe /health and /alive; the Worker serves no other HTTP.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

// The database through the Aspire client, as ApiService has it. The context refuses to build without the pinned
// Identity schema, even in a host that never touches an account (PP1.1).
// One context per scope, never pooled: a site job's scope has its own site (PP2.5).
builder.Services.AddAccountsIdentitySchema();
builder.Services.AddPantryPressDbContext((services, options) => options.UseSqlServer(
    services.GetRequiredService<IConfiguration>().GetConnectionString("pantrypress")
        ?? throw new InvalidOperationException("The Worker has no pantrypress connection string.")));
builder.EnrichSqlServerDbContext<PantryPressDbContext>();

// Account email needs a provider. Locally that is the development sink (PP1.2a); elsewhere there is none until Azure
// Communication Services arrives at PP13.1. Without one the Worker claims no account email: it waits in the outbox,
// visible in the backlog gauge and reported here as degraded, while everything else the Worker does carries on (PP1.12).
var sendsEmail = builder.Environment.IsDevelopment();
if (sendsEmail)
{
    builder.Services.AddDevelopmentMailSink(options => builder.Configuration.GetSection("DevelopmentMail").Bind(options));
}
else
{
    builder.Services.AddHealthChecks().AddCheck(
        "account-email",
        () => HealthCheckResult.Degraded("No email provider yet (PP13.1): account email waits in the outbox."));
}

// Accounts' share of the Worker: account email from the outbox, with the links in it (PP1.10b), and the housekeeping
// the sweep runs (PP1.12). Data protection carries the API's application name, because the API checks the links made
// here. Locally both read the developer profile's key ring; anywhere else the two hosts share a ring only once PP19
// persists it behind Key Vault, so a Worker that would send email there without one refuses to start - every link it
// made would fail at the API.
builder.Services.AddDataProtection().SetApplicationName("PantryPress");
builder.Services.AddOptions<KeyManagementOptions>()
    .Validate(
        keys => builder.Environment.IsDevelopment() || !sendsEmail || keys.XmlRepository is not null,
        "Account email outside Development needs the key ring ApiService shares (PP19): links made here would not validate there.")
    .ValidateOnStart();
builder.Services.AddAccountsWorker(builder.Configuration.GetSection("Accounts"), sendsEmail);

// The outbox dispatcher and the sweep that also purges expired idempotency keys and sign-outs (PP1.10, PP1.12).
builder.Services.AddIdempotency(builder.Configuration.GetSection("Idempotency"));
builder.Services.AddOutboxDispatcher(builder.Configuration.GetSection("Outbox"));

var app = builder.Build();
app.MapDefaultEndpoints();

app.Run();

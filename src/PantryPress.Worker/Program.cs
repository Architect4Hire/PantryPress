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
builder.Services.AddAccountsIdentitySchema();
builder.AddSqlServerDbContext<PantryPressDbContext>("pantrypress");

// The outbox dispatcher and the sweep that also purges expired idempotency keys (PP1.10).
builder.Services.AddIdempotency(builder.Configuration.GetSection("Idempotency"));
builder.Services.AddOutboxDispatcher(builder.Configuration.GetSection("Outbox"));

var app = builder.Build();
app.MapDefaultEndpoints();

app.Run();

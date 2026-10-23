using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Platform;
using PantryPress.MigrationService;

// One-shot: no HTTP and no /health or /alive. It applies migrations and exits; dependents WaitForCompletion on it,
// so its exit code is its health signal. Host apps never apply migrations (OPS-003).
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();
builder.AddPantryPressDbContext();

// The operator's PlatformAdmin list (ACCT-005). This one-shot job, started by the operator, is the only process that
// can assign the platform role: no API and no browser can reach it. With no list configured it changes nothing.
builder.Services.AddPlatformAdministration(builder.Configuration.GetSection("Platform"));
builder.Services.AddMigrationWorker();

var host = builder.Build();

// Resolve before Run, which disposes the host on the way out.
var outcome = host.Services.GetRequiredService<MigrationOutcome>();
await host.RunAsync();
return outcome.ExitCode;

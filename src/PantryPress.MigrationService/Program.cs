using PantryPress.Domain.Managers.Time;
using PantryPress.MigrationService;

// One-shot: no HTTP and no /health or /alive. It applies migrations and exits; dependents WaitForCompletion on it,
// so its exit code is its health signal. Host apps never apply migrations (OPS-003).
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();
builder.AddPantryPressDbContext();
builder.Services.AddMigrationWorker();

var host = builder.Build();

// Resolve before Run, which disposes the host on the way out.
var outcome = host.Services.GetRequiredService<MigrationOutcome>();
host.Run();
return outcome.ExitCode;

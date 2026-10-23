using PantryPress.Domain.Managers.Time;

// One-shot: no HTTP and no /health or /alive. From PP0.6 it applies migrations and exits, dependents wait for it to
// complete, and its exit code is its health signal.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

var host = builder.Build();
host.Run();

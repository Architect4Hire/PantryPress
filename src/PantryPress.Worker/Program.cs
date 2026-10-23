using PantryPress.Domain.Managers.Time;

// A web host only so the orchestrator can probe /health and /alive; the Worker serves no other HTTP.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

var app = builder.Build();
app.MapDefaultEndpoints();

app.Run();

using PantryPress.Domain.Managers.Time;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationClock();

var app = builder.Build();
app.MapDefaultEndpoints();

app.Run();

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Routes and clusters are declared in configuration (gateway.md). Destinations name Aspire resources (http://admin)
// and resolve through service discovery, so no service address is written down. Development adds a catch-all route
// to the admin dev server; production has no SPA route yet.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();

using PantryPress.Gateway.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Routes and clusters are declared in configuration (gateway.md). Destinations name Aspire resources
// (http://apiservice, http://admin) and resolve through service discovery, so no service address is written down. The
// admin API route ships in every environment; Development adds a catch-all route to the admin dev server, and
// production has no SPA route yet (PP19 serves the built files).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddApiProblems();
builder.Services.AddApiReachabilityCheck();

var app = builder.Build();

// Every failure the gateway produces gets the API's problem shape: an unhandled fault here, and YARP's bodiless 502 or
// 504 when a forwarded request never reached ApiService.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapDefaultEndpoints();
app.MapApiFallback();
app.MapReverseProxy();

app.Run();

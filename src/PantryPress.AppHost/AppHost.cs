using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// PantryPress's own data resources (PP-D16): nothing here is shared with CreatorPantry. Volume names carry the
// pantrypress- prefix so they never collide with another product's volumes on the same Docker host.
// Passwords are generated once and persisted to this AppHost's user-secrets, so a persistent volume never
// outlives its password. Ports and connection strings are assigned by Aspire.
var sqlPasswordParameter = builder.AddParameter(
    "sql-password",
    new GenerateParameterDefault { MinLength = 24, MinLower = 1, MinUpper = 1, MinNumeric = 1 },
    secret: true,
    persist: true);

var redisPasswordParameter = builder.AddParameter(
    "redis-password",
    new GenerateParameterDefault { MinLength = 24, Special = false },
    secret: true,
    persist: true);

var sql = builder.AddSqlServer("sql", sqlPasswordParameter)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("pantrypress-sql-data");

var db = sql.AddDatabase("pantrypress");

builder.AddRedis("redis", password: redisPasswordParameter)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("pantrypress-redis-data")
    .WithPersistence();

builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume("pantrypress-azurite-data"))
    .AddBlobs("blobs");

// The migration service applies migrations once the database exists (waiting on the database also waits on the
// server), then exits. It has no endpoints: its exit code is its health signal. Everything else starts only after it
// finishes with exit code 0 (OPS-003); a failed run leaves dependents unstarted. Other references arrive with their
// consumers.
var migrations = builder.AddProject<Projects.PantryPress_MigrationService>("migrationservice")
    .WithReference(db)
    .WaitFor(db);

// Long-running projects report readiness through /health (ServiceDefaults).
builder.AddProject<Projects.PantryPress_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);
builder.AddProject<Projects.PantryPress_Site>("site")
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);
builder.AddProject<Projects.PantryPress_Worker>("worker")
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);
var gateway = builder.AddProject<Projects.PantryPress_Gateway>("gateway")
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations)
    .WithUrls(context =>
    {
        // Browsers resolve *.localhost to loopback, so the admin app opens at app.localhost (SITE-009).
        foreach (var url in context.Urls)
        {
            url.Url = new UriBuilder(url.Url) { Host = "app.localhost" }.Uri.ToString();
        }
    });

// The admin SPA's dev server (PP-D08: client-rendered). Browsers are meant to reach it through the gateway's catch-all
// YARP route, so cookies and antiforgery behave as they will in production. That route lives in the gateway's
// appsettings.Development.json and names this resource ("admin"), so both exist only in local Development runs: in
// production the gateway serves the built files, and publishing must not produce an admin container. The gateway does
// not wait for the dev server, so an admin compile error never blocks the gateway's own routes.
if (builder.ExecutionContext.IsRunMode && builder.Environment.IsDevelopment())
{
    var admin = builder.AddJavaScriptApp("admin", "../web", "start:admin")
        .WithHttpEndpoint()
        .WithEnvironment("NG_CLI_ANALYTICS", "false")
        .WithHttpHealthCheck("/")
        .WaitForCompletion(migrations)
        .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);
    admin.WithArgs(context =>
    {
        // npm needs the separator to hand --port to ng serve rather than read it as its own option.
        context.Args.Add("--");
        context.Args.Add("--port");
        context.Args.Add(admin.GetEndpoint("http").Property(EndpointProperty.TargetPort));
    });

    gateway.WithReference(admin);
}

builder.Build().Run();

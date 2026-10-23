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

// Who holds PlatformAdmin (ACCT-005): email addresses the operator keeps in their own secret store, passed only to
// the migration job, which is the one process that can assign the role. A secret parameter, so the list is masked in
// the dashboard and published as a parameter rather than baked into the manifest: locally its value comes from this
// AppHost's user-secrets or environment ("Platform:Admins"), and PP19's IaC supplies it from Key Vault. The job always
// gets the variable; an empty list leaves platform roles exactly as they are, so a lost secret can never lock the
// operators out.
var platformAdminsParameter = builder.AddParameter(
    "platform-admins",
    () => builder.Configuration["Platform:Admins"] ?? string.Empty,
    secret: true);

migrations.WithEnvironment("Platform__Admins", platformAdminsParameter);

// The secret the gateway proves itself to the API with before the API reads any forwarded identity (PP1.6). Generated
// once and persisted to this AppHost's user-secrets, published as a parameter rather than baked into the manifest
// (PP19's IaC supplies it from Key Vault). Both processes get the same value and nothing else does: without it a
// caller that can reach ApiService directly could name any account it liked.
var gatewaySecretParameter = builder.AddParameter(
    "gateway-identity-secret",
    new GenerateParameterDefault { MinLength = 48, Special = false },
    secret: true,
    persist: true);

// Long-running projects report readiness through /health (ServiceDefaults).
var apiService = builder.AddProject<Projects.PantryPress_ApiService>("apiservice")
    .WithReference(db)
    .WithEnvironment("Gateway__SharedSecret", gatewaySecretParameter)
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);
builder.AddProject<Projects.PantryPress_Site>("site")
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);
builder.AddProject<Projects.PantryPress_Worker>("worker")
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);
// The gateway forwards the admin app's /api/v1 calls to ApiService (PP1.5), so it needs the reference for service
// discovery and reports whether the API is reachable in its own /health. It does not wait for the API: an API restart
// must not keep the admin app from loading, exactly as it does not wait for the admin dev server.
var gateway = builder.AddProject<Projects.PantryPress_Gateway>("gateway")
    .WithReference(apiService)
    .WithEnvironment("Gateway__SharedSecret", gatewaySecretParameter)
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations)
    .WithUrls(context =>
    {
        // Browsers resolve *.localhost to loopback, so the admin app opens at app.localhost (SITE-009).
        foreach (var url in context.Urls)
        {
            var rewritten = new UriBuilder(url.Url) { Host = "app.localhost" };
            url.Url = rewritten.Uri.ToString();

            // The BFF session cookie is `__Host-` prefixed and Secure (PP-D13, PSEC-003), and a browser will not keep
            // it over plain HTTP. So the dashboard offers the HTTPS address and keeps the plain one in the details:
            // signing in locally works the way it works in production.
            if (string.Equals(rewritten.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
            {
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            }
        }
    });

// Links in account emails open the admin app (PP1.2). The origin is set here, never taken from a request's Host header:
// locally the gateway at app.localhost, elsewhere the deployed admin origin from a parameter.
var isLocalDevelopment = builder.ExecutionContext.IsRunMode && builder.Environment.IsDevelopment();
if (!isLocalDevelopment)
{
    apiService.WithEnvironment("Accounts__AppBaseUrl", builder.AddParameter("app-base-url"));
}

// The admin SPA's dev server (PP-D08: client-rendered). Browsers are meant to reach it through the gateway's catch-all
// YARP route, so cookies and antiforgery behave as they will in production. That route lives in the gateway's
// appsettings.Development.json and names this resource ("admin"), so both exist only in local Development runs: in
// production the gateway serves the built files, and publishing must not produce an admin container. The gateway does
// not wait for the dev server, so an admin compile error never blocks the gateway's own routes.
if (isLocalDevelopment)
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

    // The API's own documentation, from the Aspire dashboard (PP1.5). Development only, like the documents it reads.
    apiService.WithUrlForEndpoint("http", endpoint => new ResourceUrlAnnotation
    {
        Url = $"{endpoint.Url}/scalar/v1", // the admin surface's document (ApiSurfaces.AdminV1Document)
        DisplayText = "API reference",
    });

    // Locally, email links open the admin app through the gateway at app.localhost.
    apiService.WithEnvironment(
        "Accounts__AppBaseUrl",
        ReferenceExpression.Create($"https://app.localhost:{gateway.GetEndpoint("https").Property(EndpointProperty.Port)}"));

    // The development email sink's catcher (external.md: every provider has a local fake). Mail sent by the local
    // gateway lands here and is read in its web UI; nothing leaves the machine. Messages live only as long as the
    // container. Azure Communication Services replaces it outside Development (PP13.1).
    var mail = builder.AddContainer("mail", "axllent/mailpit", "v1.31.2")
        .WithEnvironment("MP_DISABLE_VERSION_CHECK", "true")
        .WithHttpEndpoint(targetPort: 8025, name: "http")
        .WithEndpoint(targetPort: 1025, name: "smtp")
        .WithHttpHealthCheck("/readyz", endpointName: "http");

    // ApiService sends account email through the sink (PP1.2). It does not wait for the catcher: sends retry, and a
    // missed confirmation email is recovered by asking for another.
    var smtp = mail.GetEndpoint("smtp");
    apiService
        .WithEnvironment("DevelopmentMail__Host", smtp.Property(EndpointProperty.Host))
        .WithEnvironment("DevelopmentMail__Port", smtp.Property(EndpointProperty.Port));
}

builder.Build().Run();

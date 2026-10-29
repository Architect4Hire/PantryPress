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

var redis = builder.AddRedis("redis", password: redisPasswordParameter)
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

// Long-running projects report readiness through /health (ServiceDefaults). The API refreshes the public tier's host map
// in Redis when it creates a site (PP2.12), so it holds a Redis reference too.
var apiService = builder.AddProject<Projects.PantryPress_ApiService>("apiservice")
    .WithReference(db)
    .WithReference(redis)
    .WithEnvironment("Gateway__SharedSecret", gatewaySecretParameter)
    .WithHttpHealthCheck("/health")
    .WaitFor(redis)
    .WaitForCompletion(migrations);

// The anonymous public site API (ARCH-003; PP2.12). It resolves each request's site from its Host through the host map in
// Redis, reading the database only on a miss, so it needs both. Browsers resolve every name under localhost to loopback, so a
// site created locally answers at {slug}.localhost on this resource's port (SITE-009).
var site = builder.AddProject<Projects.PantryPress_Site>("site")
    .WithReference(db)
    .WithReference(redis)
    .WithHttpHealthCheck("/health")
    .WaitFor(redis)
    .WaitForCompletion(migrations);
// The Worker delivers the transactional outbox - account email included (PP1.10b) - and sweeps expired rows (PP1.10), so
// it needs the database; like the API, it starts only once the migrations are in.
var worker = builder.AddProject<Projects.PantryPress_Worker>("worker")
    .WithReference(db)
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
        // The platform home is the local zone's apex, localhost, on the gateway's own port (PP-D19); sites are
        // {slug}.localhost on the Site's (SITE-009). Named here, so the dashboard links to the one host the gateway
        // answers.
        foreach (var url in context.Urls)
        {
            var rewritten = new UriBuilder(url.Url) { Host = "localhost" };
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

// Links in account emails open the admin app (PP1.2). The Worker writes them, since it sends account email from the
// outbox (PP1.10b). The origin is set here, never taken from a request's Host header: locally the gateway at the
// apex, localhost, elsewhere the deployed platform home's origin from a parameter (PP-D19).
var isLocalDevelopment = builder.ExecutionContext.IsRunMode && builder.Environment.IsDevelopment();
// Invitation links open it too (PP2.11), so the Worker writes both from the one origin, under the one key (PP2.14).
if (!isLocalDevelopment)
{
    var appBaseUrl = builder.AddParameter("app-base-url");
    worker.WithEnvironment("Accounts__AppBaseUrl", appBaseUrl);
}

// The zone a new site's own address sits under, {slug}.{zone} (SITE-003; PP2.10). Locally it is localhost, which browsers
// resolve for every name under it (SITE-009); elsewhere a parameter, because the dev deployment is dev.pantrypress.app and
// production pantrypress.app. Never taken from a request.
if (isLocalDevelopment)
{
    apiService.WithEnvironment("Sites__SubdomainZone", "localhost");
}
else
{
    apiService.WithEnvironment("Sites__SubdomainZone", builder.AddParameter("site-subdomain-zone"));
}

// The admin app's dev server (PP-D08: the dashboard is client-rendered; since PPL.2 it also renders the public paths
// per request, as the production build prerenders them). Browsers are meant to reach it through the gateway's catch-all
// YARP route, so cookies and antiforgery behave as they will in production. That route lives in the gateway's
// appsettings.Development.json and names this resource ("admin"), so both exist only in local Development runs: in
// production the gateway serves the built files, and publishing must not produce an admin container. The gateway does
// not wait for the dev server, so an admin compile error never blocks the gateway's own routes.
if (isLocalDevelopment)
{
    // The accounts and sites a developer's machine starts with (PP3.9a), seeded by the migration job after the
    // platform-role list. Sign-up stays closed until PPL.13, so without this a local run has nobody to sign in as.
    // Declared here, inside the local-Development branch, like the mail catcher and the data explorers: the parameter
    // is never in a published manifest or the IaC, and a deployed job runs exactly what it ran before. A secret
    // parameter, so the password is masked in the dashboard; its value comes from this AppHost's user-secrets
    // ("DevSeed:Password") and from nowhere else. With none set the seed does nothing and says how to set one.
    var devSeedPassword = builder.AddParameter(
        "dev-seed-password",
        () => builder.Configuration["DevSeed:Password"] ?? string.Empty,
        secret: true);

    migrations
        .WithEnvironment("DevSeed__Password", devSeedPassword)

        // The seed creates its sites through the Sites module exactly as the API does (PP2.10), so this job needs the
        // zone a new site's address sits under - localhost locally (SITE-009) - and Redis, because creating a site
        // refreshes the public tier's host map (PP2.12). It never waits for Redis: the refresh is skipped if the cache
        // cannot be reached (aspire.md).
        .WithEnvironment("Sites__SubdomainZone", "localhost")
        .WithReference(redis);

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

    // The API's own documentation, from the Aspire dashboard (PP1.5): every API address opens the Scalar reference, since
    // the API's root answers nothing a browser can use. The path comes from ApiService's launchUrl ("scalar/v1", the admin
    // surface's document), which Aspire already appends to each address, so this only names the links.
    apiService.WithUrls(context =>
    {
        foreach (var url in context.Urls.Where(u => u.Endpoint is not null))
        {
            url.DisplayText = $"API reference ({url.Endpoint!.EndpointName})";
        }
    });

    // Locally, email links open the platform home through the gateway at the apex, localhost (PP-D19).
    var localAppBaseUrl = ReferenceExpression.Create(
        $"https://localhost:{gateway.GetEndpoint("https").Property(EndpointProperty.Port)}");
    worker.WithEnvironment("Accounts__AppBaseUrl", localAppBaseUrl);

    // The development email sink's catcher (external.md: every provider has a local fake). Mail the Worker sends through the
    // development email gateway lands here and is read in its web UI; nothing leaves the machine. Messages live only as long as the
    // container. Azure Communication Services replaces it outside Development (PP13.1).
    var mail = builder.AddContainer("mail", "axllent/mailpit", "v1.31.2")
        .WithEnvironment("MP_DISABLE_VERSION_CHECK", "true")
        .WithHttpEndpoint(targetPort: 8025, name: "http")
        .WithEndpoint(targetPort: 1025, name: "smtp")
        .WithHttpHealthCheck("/readyz", endpointName: "http");

    // The Worker sends account email through the sink (PP1.10b). It does not wait for the catcher: the outbox retries
    // a send that fails, with backoff, until the catcher is up.
    var smtp = mail.GetEndpoint("smtp");
    worker
        .WithEnvironment("DevelopmentMail__Host", smtp.Property(EndpointProperty.Host))
        .WithEnvironment("DevelopmentMail__Port", smtp.Property(EndpointProperty.Port));

    // Data explorers for the developer: DbGate for SQL and Redis Insight for Redis, each already connected with the
    // generated password, reached from the dashboard as children of what they browse. Local Development only, like
    // the mail catcher: a deployed PantryPress never ships a tool that reads every site's data. Session lifetime and no
    // volumes, so they keep nothing between runs; the data they show lives in the persistent containers.
    sql.WithDbGate(dbgate => dbgate.WithParentRelationship(sql));
    redis.WithRedisInsight(insight => insight.WithParentRelationship(redis));

    // The public site has no address of its own: every request names a site by its Host, and a bare localhost names none,
    // so its own address answers the neutral 404. The dashboard says where a site answers instead (SITE-009).
    site.WithUrls(context =>
    {
        foreach (var url in context.Urls.Where(u => u.Endpoint is not null))
        {
            url.DisplayText = $"Sites answer at {{slug}}.localhost ({url.Endpoint!.EndpointName})";
        }
    });
}

builder.Build().Run();

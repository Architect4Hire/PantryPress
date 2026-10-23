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

sql.AddDatabase("pantrypress");

builder.AddRedis("redis", password: redisPasswordParameter)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("pantrypress-redis-data")
    .WithPersistence();

builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume("pantrypress-azurite-data"))
    .AddBlobs("blobs");

// References and WaitFor arrive with their consumers: PP0.6 wires the migration service to SQL and makes every
// project wait for it.
// Long-running projects report readiness through /health (ServiceDefaults). The migration service is one-shot and
// has no endpoints; from PP0.6 its exit code is its health signal.
builder.AddProject<Projects.PantryPress_MigrationService>("migrationservice");
builder.AddProject<Projects.PantryPress_ApiService>("apiservice").WithHttpHealthCheck("/health");
builder.AddProject<Projects.PantryPress_Site>("site").WithHttpHealthCheck("/health");
builder.AddProject<Projects.PantryPress_Worker>("worker").WithHttpHealthCheck("/health");
builder.AddProject<Projects.PantryPress_Gateway>("gateway").WithHttpHealthCheck("/health");

builder.Build().Run();

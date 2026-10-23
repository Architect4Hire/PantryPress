var builder = DistributedApplication.CreateBuilder(args);

// Project skeleton only (PP0.3). Data resources arrive in PP0.4; migration ordering (WaitFor) in PP0.6.
builder.AddProject<Projects.PantryPress_MigrationService>("migrationservice");
builder.AddProject<Projects.PantryPress_ApiService>("apiservice");
builder.AddProject<Projects.PantryPress_Site>("site");
builder.AddProject<Projects.PantryPress_Worker>("worker");
builder.AddProject<Projects.PantryPress_Gateway>("gateway");

builder.Build().Run();

# Aspire

- `PantryPress.AppHost` is the only place resources are declared: SQL Server, Redis, Azure Storage (blob
  emulator locally), the migration service, ApiService, Site, Gateway, Worker, the admin Angular app, and the
  `pantrypress-site` Node SSR server.
- Use `add-aspire-resource` for any new resource. Dependents `WaitFor` what they need; everything waits for
  the migration service.
- Connection strings and secrets flow through Aspire references and parameters. No hard-coded endpoints.
- Local hosts: `app.localhost` and `{slug}.localhost` (SITE-009).
- ServiceDefaults provides OpenTelemetry, health checks (`/health`, `/alive`), service discovery, and
  resilience. Every service calls `AddServiceDefaults()`.
- PantryPress has its own SQL server, Redis, storage, and Key Vault. Only the Front Door profile, Container
  Apps environment, and monitoring workspace are shared with CreatorPantry (PP-D16), and only in IaC.
- Verify with `aspire run`: every resource healthy in the dashboard.

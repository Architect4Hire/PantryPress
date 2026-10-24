# Aspire

- `PantryPress.AppHost` is the only place resources are declared: SQL Server, Redis, Azure Storage (blob
  emulator locally), the migration service, ApiService, Site, Gateway, Worker, the admin Angular app, and the
  `pantrypress-site` Node SSR server.
- Developer tools exist only in local Development and are never published: the mail catcher, DbGate (child of
  `sql`), and Redis Insight (child of `redis`). Session lifetime, no volumes; `EphemeralAppHost` drops the explorers
  from integration tests. The API's dashboard links open its Scalar reference.
- Use `add-aspire-resource` for any new resource. Dependents `WaitFor` what they need; everything waits for
  the migration service.
- Connection strings and secrets flow through Aspire references and parameters. No hard-coded endpoints.
- Local hosts: `app.localhost` and `{slug}.localhost` (SITE-009).
- ServiceDefaults provides OpenTelemetry, health checks (`/health`, `/alive`), service discovery, resilience,
  and the edge hardening every host shares (`EdgeHardening/`: security response headers, trusted proxies and the
  forwarding-header strip, body size limits, the HTTPS redirect). Every service calls `AddServiceDefaults()`;
  the edge pieces are opted into per host, because what each one owes differs.
- Both health endpoints short-circuit the moment they are routed, so no edge middleware runs for a probe: no
  redirect, no host check, and no security headers on a probe's answer.
- PantryPress has its own SQL server, Redis, storage, and Key Vault. Only the Front Door profile, Container
  Apps environment, and monitoring workspace are shared with CreatorPantry (PP-D16), and only in IaC.
- Verify with `aspire run`: every resource healthy in the dashboard.

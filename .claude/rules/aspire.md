# Aspire

- `PantryPress.AppHost` is the only place resources are declared: SQL Server, Redis, Azure Storage (blob
  emulator locally), the migration service, ApiService, Site, Gateway, Worker, the admin Angular app, and the
  `pantrypress-site` Node SSR server.
- Site references the database and Redis and waits for Redis and for the migration service, which waits for the
  database (PP2.12): it resolves each request's site from its Host
  through the host map in Redis, reading the database only on a miss. ApiService references Redis too, because creating a
  site refreshes the map. Both connect through `AddCacheConnection` (ServiceDefaults), which never waits for Redis and adds
  no readiness check for it: each host carries on without the cache. Locally a site answers at `{slug}.localhost` on the
  Site resource's port (SITE-009).
- Developer tools exist only in local Development and are never published: the mail catcher, DbGate (child of
  `sql`), and Redis Insight (child of `redis`). Session lifetime, no volumes; `EphemeralAppHost` drops the explorers
  from integration tests. The API's dashboard links open its Scalar reference.
- Use `add-aspire-resource` for any new resource. Dependents `WaitFor` what they need; everything waits for
  the migration service.
- Connection strings and secrets flow through Aspire references and parameters. No hard-coded endpoints.
- Local hosts: `localhost` for the platform home through the gateway (the landing page, sign-in, and `/dashboard`;
  PP-D19, PPL.1) and `{slug}.localhost` for sites (SITE-009). Account and invitation email links open
  `https://localhost:{gateway https port}`. A browser keeps `localhost` cookies apart by host, not port, so the
  gateway's cookies also reach the other resources on `localhost`. The Site host registers no cookie handling and a test
  proves they change nothing there; the developer tools are local only.
- ServiceDefaults provides OpenTelemetry, health checks (`/health`, `/alive`), service discovery, resilience,
  and the edge hardening every host shares (`EdgeHardening/`: security response headers, trusted proxies and the
  forwarding-header strip, body size limits, the HTTPS redirect). Every service calls `AddServiceDefaults()`;
  the edge pieces are opted into per host, because what each one owes differs.
- Both health endpoints short-circuit the moment they are routed, so no edge middleware runs for a probe: no
  redirect, no host check, and no security headers on a probe's answer.
- PantryPress has its own SQL server, Redis, storage, and Key Vault. Only the Front Door profile, Container
  Apps environment, and monitoring workspace are shared with CreatorPantry (PP-D16), and only in IaC.
- Verify with `aspire run`: every resource healthy in the dashboard.

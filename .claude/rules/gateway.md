# Gateway (YARP BFF)

`PantryPress.Gateway` serves `app.pantrypress.app`: the admin SPA's static files and the admin API surface
(`/api/v1/...`) proxied to `PantryPress.ApiService`.

- In local Development the gateway is the browser's entry point at `app.localhost`; a catch-all route in
  `appsettings.Development.json` forwards the SPA to the Aspire-hosted admin dev server (PP0.7).
- Routes are declared in configuration and versioned (`/api/v1/...`). Every other path under `/api` gets a
  problem-details 404 from the gateway itself (PP1.5), so nothing falls through to the SPA: the publishing API
  is not routed through the gateway (it has its own Front Door route), the public site API answers on site
  hosts, and a version nobody declared does not exist. Route orders are explicit.
- The gateway owns the session cookie, antiforgery, and header hygiene: strip `X-Forwarded-*` from the
  client, strip any identity headers, add the authenticated user context for the API.
- CORS is closed: same-origin only.
- Rate limits per user and per IP for authentication endpoints.
- The gateway does no business logic and no database access.
- Health checks come from ServiceDefaults; the gateway reports API reachability in `/health` - any HTTP answer
  counts as reachable, unreachable is degraded rather than unhealthy, and `/alive` never calls the API.

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
- Header hygiene is the first middleware (PP1.7): `Forwarded` and every `X-Forwarded-*` spelling come off every
  inbound request, on every path, and the proxy declares its own with `Set`, never append. The gateway therefore
  trusts nobody about the client address until PP1.8 adds the trusted-proxy step, which runs ahead of the strip.
- The session is the gateway's own (PP1.6, `auth.md`): four routes at `/api/v1/account/session` that it answers itself
  rather than forwarding, two `__Host-` cookies (a complete session and a half-finished one waiting for a second
  step), and one typed client to ApiService's `api/internal/v1` surface for the checks themselves. The gateway holds
  no rules: it relays the API's problem document for a refusal a person is meant to read, and answers its own 502 for
  anything else. A half-finished session is refused here, never forwarded.
- On every request forwarded to the admin API the gateway removes the client's `X-PP-*` headers and the `Cookie`
  header, then adds the account, the session's security stamp, and its shared secret. The whole identity layer keys on
  the `api-v1` route id, so the host refuses to start when configuration has no such route.
- CORS is closed: same-origin only, a default policy that allows no origin, so a preflight is answered by the gateway
  with no `Access-Control-Allow-*` header and is never forwarded. A state-changing request whose `Origin` is not this
  exact scheme, host, and port - or is opaque - is refused: 403, `cross-origin-blocked`.
- Antiforgery guards every `POST`, `PUT`, `PATCH`, and `DELETE` under `/api/v1`, the gateway's own session routes
  included (PP1.7). The cookie token is `__Host-pp-antiforgery` (`HttpOnly`); the request token is `__Host-pp-xsrf`,
  the one cookie the admin app reads, and it comes back in `X-PP-Xsrf` - the names PP2 gives Angular's
  `withXsrfConfiguration`. Both are `Secure`, `Path=/`, `SameSite=Strict`, no `Domain`. A read mints a pair when the
  browser has none, and so does a refusal (400, `antiforgery-failed`), so a stale token costs a retry, not a reload.
  The token is bound to this origin, not to an account: the gateway calls `UseAuthentication()` itself, after these
  checks, because a `WebApplication` would otherwise place it first and bind every token to the session it was minted
  under. Over plain HTTP there is no token and no write, only reads.
- Rate limits per user and per IP for authentication endpoints.
- The gateway does no business logic and no database access.
- Health checks come from ServiceDefaults; the gateway reports API reachability in `/health` - any HTTP answer
  counts as reachable, unreachable is degraded rather than unhealthy, and `/alive` never calls the API.

# Gateway (YARP BFF)

**Detail: `docs/requirements/pantrypress-gateway-spec.md`.** Read its text before changing the gateway.

`PantryPress.Gateway` serves the platform home on the apex `pantrypress.app` (PP-D19): the `pantrypress-admin` app's
files and the admin API (`/api/v1/...`) proxied to ApiService. `app.` and `www.` are 301s at Front Door, never gateway
hosts. Locally it is `localhost`, with a catch-all to the admin dev server.

## Invariants

- **Hosts and paths.** A request not addressed to one of `Gateway:Hosts` gets the neutral problem 404 (no framework
  host filtering). Every undeclared path under `/api` is the gateway's own problem 404; nothing falls through to the SPA.
  Routes are in configuration, versioned, with explicit orders.
- **Pages (PPL.3).** A signed-in visitor's `GET`/`HEAD` of `/` or `/pricing` is 302 to `/dashboard`, decided from the
  gateway's own cookie. `Gateway:AdminApp` lists must match the admin app's `PUBLIC_PATHS` and
  `SIGNED_IN_REDIRECT_PATHS` (test). Outside Development the gateway serves the build itself and refuses to start
  without it or on a document with inline script.
- **Caching.** Every HTML answer is `no-cache`; only fingerprinted files are immutable; an answer that sets or clears a
  cookie is `no-cache, no-store`. No price or account data in a prerendered page.
- **Header hygiene.** `Forwarded`, every `X-Forwarded-*`, the Front Door family, and the rewriting proxies' originals
  are stripped from every inbound request. The trusted-proxy step runs ahead of that, believing only `Edge:TrustedProxies`
  peers; a deployed host refuses to start unless it names a peer or sets `TrustNobody`. The proxy sets its own with
  `Set`, never append.
- **Transport and headers.** Plain HTTP is 307 to HTTPS; HSTS (a year, `includeSubDomains`, no `preload`) outside
  Development; probes exempt. Every answer carries the PSEC-002 headers, refusals included. Bodies capped at 1 MiB on the
  declared length.
- **Session (PP1.6, `auth.md`).** Four routes at `/api/v1/account/session` answered by the gateway itself, two `__Host-`
  cookies, one typed client to `api/internal/v1`. The gateway holds no rules: it relays the API's problem document or
  answers its own 502. A half-finished session is never forwarded.
- **Forwarding.** Remove the client's `X-PP-*` headers and `Cookie`; always add the shared secret; add account, stamp,
  and session id for a complete session; mark `X-PP-Via: proxy`. Keyed on the `api-v1` route id.
- **Writes.** CORS closed. A write whose `Origin` is not this exact origin is 403 `cross-origin-blocked`. Antiforgery
  guards every `POST`/`PUT`/`PATCH`/`DELETE` under `/api/v1` (`__Host-pp-antiforgery`, `__Host-pp-xsrf`, `X-PP-Xsrf`);
  `UseAuthentication()` runs after those checks.
- **Limits.** Per-address limits on sign-in and on session checks; ApiService keeps ceilings behind them.
- No business logic, no database access. `GatewayClock` is the one reader of `TimeProvider`. `/health` reports API
  reachability (degraded, not unhealthy); `/alive` never calls the API.

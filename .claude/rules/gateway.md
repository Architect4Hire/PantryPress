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
- Header hygiene (PP1.7, PP1.8): `Forwarded`, every `X-Forwarded-*` spelling, the Azure Front Door family
  (`X-Azure-FDID`, `X-Azure-ClientIP`, `X-Azure-SocketIP`) and the rewriting proxies' originals (`X-Original-Host`,
  `X-Original-URL`, `X-Real-IP`, `X-ProxyUser-Ip`) come off every inbound request, on every path, and the proxy
  declares its own with `Set`, never append. The trusted-proxy step runs **ahead** of that strip and reads those
  headers only from a peer configured in `Edge:TrustedProxies` (empty by default, which means believe nobody), with
  `ForwardLimit` counting the hops that may be believed. That step is what makes the write path work behind Front
  Door, where TLS ends at the edge: without it `Request.IsHttps` is false and `Request.Host` is the container's, so
  antiforgery and the same-origin check would refuse every admin write, per-address limits collapse into one bucket
  tighter than the ceiling they replaced, and no HSTS goes out. None of that shows in a local run, where Kestrel
  terminates TLS itself, so **a deployed host refuses to start** unless it names a peer or sets
  `Edge:TrustedProxies:TrustNobody`. A trusted peer may also name the host, but only one from `Gateway:Hosts`; the
  public tier does not grant even that, because there the host is the tenant.
- The admin surface is HTTPS. A plain-HTTP request is answered 307 to the same address over TLS, and every answer
  outside Development carries HSTS for a year with `includeSubDomains` and no `preload`. Health probes are exempt
  from the redirect, and the redirect is registered only where the host can tell what scheme the browser used -
  Development, or a deployment that named its proxy - because a configured HTTPS port without a trusted proxy
  redirects the edge back to the address it just came from, for ever.
- Every answer carries the PSEC-002 headers: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: no-referrer`, a deny-all `Permissions-Policy`, and a content-security policy - `default-src
  'none'` plus `sandbox` for an `/api` answer, the admin app's own policy for a document (looser in Development for
  the dev server's sake). They are written on the response's start, so a refusal that never reached an endpoint
  carries them too.
- A request is addressed to one of `Gateway:Hosts` or it gets the gateway's neutral problem 404 - a tenant site host
  included, since Front Door routes those to the public site. The framework's host filtering is deliberately not
  used: its bodiless 400 would be the one answer under `/api` that is not a problem document.
- Bodies are capped at 1 MiB, refused on the declared length before anything reads one - the gateway would otherwise
  stream an over-sized body to ApiService and turn the refusal into a 502. `Gateway:PublicOrigin`, when set, is the
  origin the same-origin check compares against, so that check does not depend on proxy configuration being right.
- The session is the gateway's own (PP1.6, `auth.md`): four routes at `/api/v1/account/session` that it answers itself
  rather than forwarding, two `__Host-` cookies (a complete session and a half-finished one waiting for a second
  step), and one typed client to ApiService's `api/internal/v1` surface for the checks themselves. The gateway holds
  no rules: it relays the API's problem document for a refusal a person is meant to read, and answers its own 502 for
  anything else. A half-finished session is refused here, never forwarded.
- On every request forwarded to the admin API the gateway removes the client's `X-PP-*` headers and the `Cookie`
  header, then adds its shared secret - to every one, signed in or not, because ApiService believes the browser's address
  only behind it and its per-address limits on sign-up and password reset depend on that (PP1.12) - and, for a complete
  session, the account, the session's security stamp, and the session's id. The whole identity layer keys on the
  `api-v1` route id, so the host refuses to start when configuration has no such route.
- `GET /session` asks ApiService (`sessions/current`) whether a complete session still stands rather than reading its own
  cookie, and clears the cookie on `session-ended`; `DELETE /session` revokes the session at ApiService (`sessions/end`)
  before clearing the cookies, and still clears them if the API cannot be asked. A sign-in that can no longer be finished
  (`sign-in-expired`) takes its half-finished cookie with it. An unreachable or slow API is the gateway's own 502, never
  an unhandled fault (PP1.12). Every session route declares each status it can answer.
- The gateway has no Domain reference, so `GatewayClock` is its one reader of `TimeProvider`, the counterpart of the
  Domain's `IClock`; the cookie handlers read the same provider.
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
- Rate limits per address on the two sign-in routes (`Gateway:SessionPerIpPerMinute`, default 20), which is what the
  trusted-proxy step was owed for: before it, partitioning by address would have put every request behind Front
  Door's own address in one bucket. ApiService keeps a global ceiling behind that as a backstop, and partitions its
  anonymous account routes by the address the gateway vouches for. The current-session read and sign-out each ask the
  API too, so they are limited per address as well (`Gateway:SessionChecksPerIpPerMinute`, default 120), and on
  ApiService they have a ceiling of their own, never the sign-in one: page loads must not crowd out sign-in, nor a busy
  minute refuse a sign-out (PP1.12). A 429 from that backstop is relayed with its `Retry-After`.
- Every request the proxy forwards carries `X-PP-Via: proxy`, and ApiService's internal surface refuses a request that
  carries it, so nothing forwarded can reach that surface whatever a route mistake might let through; only the
  gateway's session client, which never sets it, can (PP1.12). A route that turns two-step sign-in on answers with
  `X-PP-Second-Step: passed` beside its new stamp, and the gateway re-issues the caller's cookie with the second-step
  claim, since the request itself gave a valid code.
- The gateway does no business logic and no database access.
- Health checks come from ServiceDefaults; the gateway reports API reachability in `/health` - any HTTP answer
  counts as reachable, unreachable is degraded rather than unhealthy, and `/alive` never calls the API.

# Gateway (YARP BFF)

`PantryPress.Gateway` serves the platform home on the zone's apex, `pantrypress.app` (PP-D19, ADR 0019): the
`pantrypress-admin` app's files - the prerendered landing page at `/` and `/pricing`, the legal pages, the sign-in and
sign-up screens, and the dashboard under `/dashboard/…` - and the admin API surface (`/api/v1/...`) proxied to
`PantryPress.ApiService`. `app.pantrypress.app` and `www.pantrypress.app` are 301s to the apex at Front Door, never
gateway hosts: the gateway answers either with its neutral 404 (PPL.1). **Deployed, the Terraform still names
`app.<zone>` until PP9.1a and PP9.2 move the records, the Front Door route, and the gateway's settings to the apex.**

- In local Development the gateway is the browser's entry point at `localhost` (the local zone's apex, on the
  gateway's own port; sites are `{slug}.localhost` on the Site resource's); a catch-all route in
  `appsettings.Development.json` forwards the SPA to the Aspire-hosted admin dev server (PP0.7). Dev is
  `dev.pantrypress.app`.
- **A signed-in visitor never sees marketing (PPL.3).** A `GET` or `HEAD` of `/` or `/pricing` carrying a complete
  session ticket is answered 302 to `/dashboard` (`no-store`) before any document is read, in every environment, ahead
  of the dev server too. The gateway decides it from its own cookie alone and asks ApiService nothing; the dashboard's
  guard confirms the session. A half-finished ticket is not a session here either, nor is one past its sliding or
  absolute lifetime. The legal pages are public but never redirected. A public path spelled as the router would also
  read it (`/pricing/`, `/pricing;x=1`, `//`) is first answered 301 to its own spelling, query kept, so no spelling
  reaches the landing page past this check.
- **The pages and their lists (PPL.3).** `Gateway:AdminApp` holds `PublicPaths`, `SignedInRedirectPaths`, and
  `SignedInHome`; a test reads the admin app's `public-paths.ts` as text and fails when they drift from `PUBLIC_PATHS`
  and `SIGNED_IN_REDIRECT_PATHS`. The host refuses to start on a list that could loop (the home redirected or public, a
  redirected path that is not public) or a malformed path. Outside Development, `Gateway:AdminApp:Root` names the admin
  app's build (`dist/pantrypress-admin/browser`; the image puts it there, PP19) and the gateway serves it itself, at the
  lowest route order, `GET` and `HEAD` only:
  - a public path gets its prerendered `{path}/index.html`;
  - any other page path - everything under `/dashboard`, a last segment with no dot, or one ending `.html`, so
    `/index.html` cannot step around the redirect - gets `index.csr.html`, and the app draws the page or its not-found
    page (a 200);
  - `/api` in any case and the probe paths (`/health`, `/alive`, and below) are never pages: deployed, the probes are
    not mapped until PP19.2, and they skip the host check and the HTTPS redirect;
  - any other path is a file of the build or the gateway's problem 404, never a document, so a stale chunk fails as
    missing. Dotfiles and paths that leave the root are never served.

  It refuses to start without the build, without a public path's document or `index.csr.html`, or when a document
  carries inline script or an inline event handler, which `script-src 'self'` would block; `npm run build` checks the
  same before anything ships (`check:admin-build`). In Development `Root` must be unset and the `admin-spa` catch-all
  serves every page; the two never coexist.
- **Documents are never shared-cached.** Every HTML answer on the apex is `Cache-Control: no-cache` (the dev server's
  too, set on the way through), because `/` answers a signed-in visitor differently; only fingerprinted files
  - Angular's bundles at the root (`main-`, `chunk-`, `styles-`, `polyfills-`, `worker-`) and `media/name-HASH.ext` -
  are `public, max-age=31536000, immutable`; anything copied from `public/` keeps its own name and is `no-cache`.
  Document tags are their content's hash. Any page or file answer that sets or clears a cookie is
  `no-cache, no-store`, whatever it would otherwise be. The Front Door route
  for the apex caches nothing else (PP9.2). Prerendered pages carry no price, plan figure, or account data.
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
  outside Development carries HSTS for a year with `includeSubDomains` and no `preload`. Sent from the apex, that
  covers every host under the zone - every blog subdomain and `dev.` included - so every web host under the zone must
  serve HTTPS (ADR 0019); custom domains are outside the zone and untouched. Health probes are exempt
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

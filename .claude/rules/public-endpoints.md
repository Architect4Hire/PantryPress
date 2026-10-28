# Public (anonymous) endpoints

Lives in `PantryPress.Site`. Use `add-public-endpoint`.

- Site comes from the Host header only, through `SiteHost` (PSEC-004). Payloads never carry a site id.
- Unknown host: neutral platform 404, the same problem document (`not-found`, `no-store`) an unknown path on a real site
  gets, whatever the request's `Accept` asks for, so a reader cannot tell a real host from a guess. A pending host, an IP literal, and a malformed host or port
  get it too; the last two are never looked up (PP2.12).
- A site that is not open to readers gets its status answer from every public controller, ahead of its own work
  (SITE-008, SEO-007; PP2.13): `SiteStatusFilter` is registered globally, so no endpoint can forget it. Being set up is 503
  `site-coming-soon` with `Retry-After: 30`; suspended and offline are one answer, 503 `site-unavailable` with
  `Retry-After: 3600`, so neither an operator's action nor a failed payment shows; deleted is 410 `site-gone`. Each is the
  neutral problem document, `no-store`, with nothing of the site's in it. Read-only is answered exactly like live. An
  unknown path keeps the neutral 404 whatever the site's status. The status comes before any redirect a host would owe.
- `GET /api/public/v1/site` is the site context (PP2.13): name, tagline, locale, canonical host, and whether the request
  used it, cached `public, max-age=60`; never a site id, slug, time zone, status, setting, or member. It reports the
  canonical host and does not redirect: the page's 301 is the SSR server's (PP7.13a). It varies by Host alone, so every
  cache of it - the edge, the SSR server's, page data - keys on the Host or the site id (PP7.12, PP9.4).
- Every route sits under `api/public/v1` (`PublicSurfaceConvention`; the host refuses to start otherwise), and the
  `public-v1` OpenAPI document is served in Development only, on a site's host like any other request.
- The site is resolved once per request, before any endpoint, and read from `IPublicSiteContext`; once the host map
  holds a host, resolving it makes no SQL query. No Identity, authentication, antiforgery, session, or cookie is
  registered on this host (PP2.12).
- Reads serve page data (Redis, then blob, plus an in-process LRU). No SQL on page-data reads (ARCH-013).
- Writes (ratings, comments, signups, contact, analytics beacon) are small JSON endpoints that:
  - validate and size-limit input, treat it as untrusted, and HTML-encode on output;
  - apply spam defense where relevant: honeypot, minimum time-to-submit, link limits, the spam challenge;
  - are rate-limited per IP, per site, and globally (PSEC-001);
  - store reader data minimized and encrypted at rest (PSEC-005);
  - never reveal whether an email is already subscribed.
- Responses set cache headers deliberately: page data cacheable, writes `no-store`.
- Every answer carries the PSEC-002 headers (PP1.8): `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy:
  strict-origin-when-cross-origin` so a blogger's outbound links are still credited, a `Permissions-Policy` that
  denies everything but `screen-wake-lock=(self)` for cook mode, HSTS outside Development, and a content-security
  policy. That policy is a strict placeholder until it is generated per site from the blogger's settings, which is
  where an ad or analytics origin earns its place (MON-005, PP9).
- Request bodies are capped at 64 KiB: everything a reader sends is small by rule. The public tier trusts no
  forwarding header from anyone - a site is resolved from the Host header alone, and proving a request came through
  Front Door rather than straight at the origin is PP9.3's. Even once a proxy is named, it may not say what host was
  asked for: `TrustedProxyOptions.ForwardHost` stays off here, because that header would otherwise choose the tenant, and
  the host refuses to start if configuration turns it on or names `AllowedHosts` (PP2.12). So Front Door must pass the
  reader's Host through to the origin unchanged, and the Container Apps ingress must accept tenant hosts (PP9.3, PP19).
- HSTS from the public tier never claims `includeSubDomains`: DOM-001 allows a custom domain at its apex, and a year
  of it from `example.com` would pin hosts PantryPress neither serves nor was given permission to speak for.
- The content-security policy served here is a placeholder. The generated per-site one belongs with the HTML, which
  comes from the SSR server, and it travels in the theme snapshot so it is deterministic (ARCH-005), versioned, and
  purged with the page. Inline content is admitted by `'sha256-…'` from that snapshot, never a nonce: output is
  byte-identical and cached at the edge, so a per-request value cannot be part of it.
- Every public endpoint gets a two-site test including host-header tricks.

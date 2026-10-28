# Public (anonymous) endpoints

Lives in `PantryPress.Site`. Use `add-public-endpoint`.

- Site comes from the host the reader asked for only, through `SiteHost` (PSEC-004, ARCH-003): the `Host` header, or -
  deployed, from PP9.3 - the header Front Door overwrites with it, believed only with the edge secret (below).
  Payloads never carry a site id.
- Unknown host: neutral platform 404, the same problem document (`not-found`, `no-store`) an unknown path on a real site
  gets, whatever the request's `Accept` asks for, so a reader cannot tell a real host from a guess. A pending host, an IP literal, and a malformed host or port
  get it too; the last two are never looked up (PP2.12). The one exception is a Host Kestrel cannot parse at all (a bad
  character, a port that is not digits, an unclosed bracket, none on HTTP/1.1): Kestrel answers its own bare 400 before
  the app runs, the same whether or not any site exists (PP2.14). The framework's host filter never runs here:
  the top-level `AllowedHosts` setting stays `*`, and the host refuses to start otherwise.
- Every host a reader can send that is new to the host map costs one SQL query and a 60-second "names nothing" entry,
  so a flood of random hosts under the zone will be bounded by PSEC-001's per-address and global limits ahead of
  resolution, with a global cap on lookups the map misses (PP9.8). Until then it is not.
- A site that is not open to readers gets its status answer from every public controller, ahead of its own work
  (SITE-008, SEO-007; PP2.13): `SiteStatusFilter` is registered globally, so no endpoint can forget it. Being set up is 503
  `site-coming-soon` with `Retry-After: 30`; suspended and offline are one answer, 503 `site-unavailable` with
  `Retry-After: 3600`, so neither an operator's action nor a failed payment shows; deleted is 410 `site-gone`. Each is the
  neutral problem document, `no-store`, with nothing of the site's in it. Read-only is answered exactly like live. An
  unknown path keeps the neutral 404 whatever the site's status. The status comes before any redirect a host would owe.
- `GET /api/public/v1/site` is the site context (PP2.13): name, tagline, locale, canonical host, and whether the request
  used it, cached `public, max-age=60`; never a site id, slug, time zone, status, setting, or member. It reports the
  canonical host and does not redirect: the page's 301 is the SSR server's (PP7.13a). It varies by the reader's host alone,
  so every cache of it - the edge, the SSR server's, page data - keys on the resolved host (the one resolution read)
  or the site id: never the inbound `Host`, `req.headers.host`, or a raw forwarding header, because behind Front Door
  every tenant's request arrives with the same origin Host (PP7.12, PP7.13a, PP9.4; PP2.14).
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
- Request bodies are capped at 64 KiB: everything a reader sends is small by rule. Forwarding headers are believed
  about the reader's address and scheme only, and only from a peer named in `Edge:TrustedProxies`; never about the
  host. `TrustedProxyOptions.ForwardHost` stays off here for good, because `X-Forwarded-Host` would otherwise choose the
  tenant, and the host refuses to start if configuration turns it on or names `Edge:TrustedProxies:AllowedHosts`
  (PP2.12).
- **How the reader's host reaches this tier when deployed (owner decision, PP2.14; built by PP9.3 and PP7.13).**
  - Front Door connects to every origin with the origin's own Container Apps address as the Host, so no tenant host
    or custom domain is ever bound or certified at Container Apps.
  - A rule set on PantryPress's own sites routes *overwrites* `X-PP-Reader-Host` with the host the reader asked for,
    and sets `X-PP-Edge` to a per-environment secret from Key Vault: the edge secret. The Front Door profile and the
    Container Apps environment are shared with CreatorPantry (PP-D16), so neither `X-Azure-FDID` nor an address
    proves a request is PantryPress's; only the edge secret does.
  - The SSR server is a Front Door origin too, so it proves the request itself before it believes the host: it reads
    `X-PP-Reader-Host` only after its own constant-time edge-secret check (the request's Host only where it runs
    without the edge, below), answers anything unproven with the neutral 404 without calling the Site, and never
    forwards `X-PP-Edge`. It then sends the host on its page-data call in `X-PP-Reader-Host`, with the internal
    service token (PP7.13). It is one token, under that one name, and the SSR server holds it and the edge secret.
  - The Site's host resolution - never `ForwardedHeadersMiddleware` - reads `X-PP-Reader-Host` only when `X-PP-Edge`
    matches the edge secret, or the token matches on the page-data call, each compared in constant time; refuses
    anything but exactly one value; normalizes and looks the host up exactly as a Host; and removes both headers so
    nothing after it reads them. No address, subnet, Front Door range, or FDID ever stands in for either secret.
  - A host runs behind the edge unless it says on purpose that it does not - the `Edge:TrustedProxies:TrustNobody`
    pattern: local Development and tests say so, and a non-Development host without the edge secret refuses to
    start otherwise. Behind the edge, a request with neither proof is answered the neutral 404, health probes
    excepted; without it, the connection's Host is read. Until PP9.3 lands, only the connection's Host is read, so a
    deployed blog answers the neutral 404.
  - An empty or unset secret or token matches nothing. During a rotation the current and the previous edge secret
    are both accepted until the rule set carries the new one. Neither value is ever logged, recorded as a telemetry
    header, or allowed to reach rendered output (Angular's request object, transfer state), as for publishing keys.
  - `X-Azure-FDID` and the `AzureFrontDoor.Backend` address ranges are filters that keep noise out, never proof.
- HSTS from the public tier never claims `includeSubDomains`: DOM-001 allows a custom domain at its apex, and a year
  of it from `example.com` would pin hosts PantryPress neither serves nor was given permission to speak for.
- The content-security policy served here is a placeholder. The generated per-site one belongs with the HTML, which
  comes from the SSR server, and it travels in the theme snapshot so it is deterministic (ARCH-005), versioned, and
  purged with the page. Inline content is admitted by `'sha256-…'` from that snapshot, never a nonce: output is
  byte-identical and cached at the edge, so a per-request value cannot be part of it.
- Every public endpoint gets a two-site test including host-header tricks.

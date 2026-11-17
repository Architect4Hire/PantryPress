# Public (anonymous) endpoints

Lives in `PantryPress.Site`. Use `add-public-endpoint`.

**Detail: `docs/requirements/pantrypress-public-endpoints-spec.md`**, including how the reader's host reaches this tier
behind Front Door (PP2.14, PP9.3). Read it before changing resolution, status answers, or headers.

## Invariants

- **Host only.** The site comes from the host the reader asked for, through `SiteHost`: the `Host` header, or deployed,
  `X-PP-Reader-Host` believed only with the edge secret (or the internal token on the page-data call), constant time,
  exactly one value, then removed. Never an address, subnet, Front Door range, or FDID. Payloads never carry a site id.
  `ForwardHost` stays off for good; `AllowedHosts` stays `*`. Secrets are never logged or rendered.
- **Neutral answers.** Unknown, pending, IP-literal, and malformed hosts get the same neutral problem 404 an unknown path
  gets (`not-found`, `no-store`), so a real host cannot be told from a guess.
- **Status first.** `SiteStatusFilter` (global) answers a closed site before any endpoint work: 503 `site-coming-soon`,
  503 `site-unavailable` (suspended and offline alike), 410 `site-gone`; read-only answers like live.
- Every route is under `api/public/v1`. The site is resolved once per request into `IPublicSiteContext`. No Identity,
  authentication, antiforgery, session, or cookie on this host.
- Caches key on the resolved host or site id, never the inbound `Host` or a raw forwarding header.
- **Reads** serve page data (Redis, blob, in-process LRU). No SQL on page-data reads.
- **Writes** (ratings, comments, signups, contact, beacon): validate and size-limit untrusted input, HTML-encode on
  output, spam defense where relevant, rate-limited per IP, per site, and globally (PSEC-001), reader data minimized and
  encrypted (PSEC-005), never reveal whether an email is subscribed, `no-store`.
- Every answer carries the PSEC-002 headers; HSTS never claims `includeSubDomains`. Bodies capped at 64 KiB. The CSP is a
  placeholder until generated per site in the theme snapshot (hash-based, never a nonce).
- Every public endpoint gets a two-site test including the host-header tricks (`tenancy.md`).

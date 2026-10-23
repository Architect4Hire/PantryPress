# Public (anonymous) endpoints

Lives in `PantryPress.Site`. Use `add-public-endpoint`.

- Site comes from the Host header only, through `SiteHost` (PSEC-004). Payloads never carry a site id.
- Unknown host: neutral platform 404. Suspended or offline site: correct status (503 with `Retry-After`,
  410 for deleted).
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
  asked for: `TrustedProxyOptions.ForwardHost` stays off here, because that header would otherwise choose the tenant.
- HSTS from the public tier never claims `includeSubDomains`: DOM-001 allows a custom domain at its apex, and a year
  of it from `example.com` would pin hosts PantryPress neither serves nor was given permission to speak for.
- The content-security policy served here is a placeholder. The generated per-site one belongs with the HTML, which
  comes from the SSR server, and it travels in the theme snapshot so it is deterministic (ARCH-005), versioned, and
  purged with the page. Inline content is admitted by `'sha256-…'` from that snapshot, never a nonce: output is
  byte-identical and cached at the edge, so a per-request value cannot be part of it.
- Every public endpoint gets a two-site test including host-header tricks.

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
- Every public endpoint gets a two-site test including host-header tricks.

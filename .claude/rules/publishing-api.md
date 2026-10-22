# Publishing API

PP-D15, API-001 to API-009, ARCH-009. Lives in `PantryPress.ApiService` under `/api/publishing/v1`.

- Authenticated by per-site publishing keys (`auth.md`). The key determines the site.
- Accepts the documented, versioned **PantryPress Publish Format**: post blocks, recipes (RCP-001/002),
  media references, SEO fields, taxonomy, and an external source reference.
- Creates or updates a draft by external source reference, optionally publishes or schedules it, reports
  status and live URL, and unpublishes.
- Goes through the same facades as the editor: validation, sanitization, wording preservation, entitlements,
  readiness checks. It never bypasses them.
- Every write takes `Idempotency-Key`. Rate-limited per key and per site, audited.
- Media by upload or HTTPS URL through the SSRF-safe fetcher and malware scanning.
- OpenAPI and a contract test suite are published (API-009). `api-contract-checker` runs on every change.
- PantryPress never calls CreatorPantry. CreatorPantry is one client among possible others.

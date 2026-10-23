# API contracts

Applies to admin API, public site API, and publishing API.

- Routes: admin `/api/v1/sites/{siteSlug}/…`; public `/api/public/v1/…`; publishing `/api/publishing/v1/…`.
  The signed-in person's own account (sign-up, password, two-factor) is account-scoped at `/api/v1/account/…` and
  never takes a site (PP1.2).
- Request and response types are records in `Contracts/` per module, never EF entities.
- Errors are RFC 9457 problem details with a stable `code` and a plain-language `title`.
- Pagination is cursor-based with `limit` (max 100).
- Every write that can be retried accepts `Idempotency-Key`; a replay returns the stored result (PP1.9).
- Dates are UTC ISO 8601; the site time zone is applied only for display and scheduling.
- Enums serialize as kebab-case strings.
- Breaking changes need a new version. Additive changes are fine.
- OpenAPI is generated for all three surfaces; the publishing API document is published (API-009).
- `api-contract-checker` runs after any contract change; contract tests cover the publishing API.

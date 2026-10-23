# API contracts

Applies to admin API, public site API, and publishing API.

- Routes: admin `/api/v1/sites/{siteSlug}/…`; public `/api/public/v1/…`; publishing `/api/publishing/v1/…`.
  The signed-in person's own account (sign-up, password, two-factor) is account-scoped at `/api/v1/account/…` and
  never takes a site (PP1.2).
- The session itself is the exception to "admin routes live in ApiService": `/api/v1/account/session` (sign in, the
  second step, read, sign out) is answered by `PantryPress.Gateway`, because the gateway holds the cookie (PP1.6,
  `gateway.md`). Its request and response records live in `PantryPress.Gateway/Contracts/`, and a test pins the path
  under the declared admin prefix and the gateway's own `/api` endpoints to exactly that set.
- `api/internal/v1` on ApiService is the gateway's alone (PP1.6): the gateway calls it to check credentials, the BFF
  never forwards it, every route on it refuses a caller without the gateway's shared secret, and the host refuses to
  start if a controller there forgets that guard. It is never routed at the edge.
- Request and response types are records in `Contracts/` per module, never EF entities.
- Errors are RFC 9457 problem details with a stable `code` and a plain-language `title`.
- Pagination is cursor-based with `limit` (max 100).
- Every write that can be retried accepts `Idempotency-Key`; a replay returns the stored result (PP1.9).
- Dates are UTC ISO 8601; the site time zone is applied only for display and scheduling.
- Enums serialize as kebab-case strings.
- Breaking changes need a new version. Additive changes are fine.
- Every route belongs to a declared API surface (route prefix plus OpenAPI document) in `ApiSurfaces`, and the
  host refuses to start when a route sits outside one: unversioned, an undeclared version, or an action whose
  absolute route escapes the prefix. A new version adds a surface; it never edits one.
- OpenAPI is generated for every surface, `internal-v1` included (Development only); the publishing API document is
  published (API-009). The gateway's four session routes declare their shapes as endpoint metadata but have no
  document of their own yet (PP2 writes the admin client).
- `api-contract-checker` runs after any contract change; contract tests cover the publishing API.

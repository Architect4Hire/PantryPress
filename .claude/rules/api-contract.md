# API contracts

Applies to admin API, public site API, and publishing API.

**Detail: `docs/requirements/pantrypress-api-contract-spec.md`** (account-level routes, the session and internal
surfaces, pre-release policy, idempotency answers). Read it before changing a route or a contract.

- Routes: admin `/api/v1/sites/{siteSlug}/…`; public `/api/public/v1/…`; publishing `/api/publishing/v1/…`. Account-level
  work (`/api/v1/sites`, `/api/v1/slug-availability`, `/api/v1/site-invitations/accept`, `/api/v1/account/…`) never sits
  beside `{siteSlug}` as a literal segment.
- `/api/v1/account/session` is answered by the Gateway (contracts in `PantryPress.Gateway/Contracts/`).
  `api/internal/v1` is the gateway's alone: unknown-route 404 without the shared secret, never forwarded or routed at
  the edge.
- Request and response types are records in each module's `Contracts/`, never EF entities.
- Errors are RFC 9457 problem details with a stable kebab-case `code` (one meaning each) and a plain-language `title`.
  A declared 400 is `ValidationProblemDetails`. A 429 carries `Retry-After`. `session-ended` is a 401 to the browser.
- The admin document declares what the gateway answers on its own; a non-repeatable write says what a retry gets.
- Admin v1 is pre-release until PP3.8/BIZ-009: a breaking change needs the OpenAPI document, contract tests, and
  decision log in the same commit. The publishing API is never pre-release. Otherwise breaking changes need a new
  version; additive changes are fine.
- Cursor pagination with `limit` (max 100).
- Retryable writes opt into `Idempotency-Key` with `[Idempotent]` (signed-in callers only, never on reads, secrets
  `[AnswersWithSecret]`, session-rotating routes, or the internal surface).
- Dates are UTC ISO 8601. Enums serialize as kebab-case strings.
- Every route belongs to a declared surface (`ApiSurfaces`, `PublicSurface`); the host refuses to start otherwise. A new
  version adds a surface, never edits one. OpenAPI is generated for every surface; the publishing API document is
  published.
- Run `api-contract-checker` after any contract change; contract tests cover the publishing API.

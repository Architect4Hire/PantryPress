# API contracts

Applies to admin API, public site API, and publishing API.

- Routes: admin `/api/v1/sites/{siteSlug}/…`; public `/api/public/v1/…`; publishing `/api/publishing/v1/…`.
  The signed-in person's own account (sign-up, password, two-factor) is account-scoped at `/api/v1/account/…` and
  never takes a site (PP1.2).
- The session itself is the exception to "admin routes live in ApiService": `/api/v1/account/session` (sign in, the
  second step, read, sign out) is answered by `PantryPress.Gateway`, because the gateway holds the cookie (PP1.6,
  `gateway.md`). Its request and response records live in `PantryPress.Gateway/Contracts/`, and a test pins the path
  under the declared admin prefix and the gateway's own `/api` endpoints to exactly that set.
- `api/internal/v1` on ApiService is the gateway's alone (PP1.6): the gateway calls it to check credentials and the second
  step, to ask whether a session still stands (`sessions/current`), and to sign a session out (`sessions/end`) (PP1.12).
  The BFF never forwards it, a caller without the gateway's shared secret is answered the unknown-route 404 before
  authentication or a rate limiter counts it, every controller there carries the guard as well, and the host refuses to
  start if one forgets it. It is never routed at the edge.
- Request and response types are records in `Contracts/` per module, never EF entities.
- Errors are RFC 9457 problem details with a stable `code` and a plain-language `title`. Codes are kebab-case and one
  meaning each; a test reads every place one is declared (PP1.12). A route's declared 400 is `ValidationProblemDetails`
  with `errors` optional, because it is either a validation failure or a plain refusal. A lockout's 429 carries
  `Retry-After`.
- The admin document declares what the gateway answers on its own before a request arrives (401 for a half-finished or
  ended session, 400 `antiforgery-failed` and 403 `cross-origin-blocked` on a write, 502 and 504), because the admin client
  is written from it. A write that cannot be repeated safely says in its description what a retry after a lost answer
  gets (PP1.12).
- Admin v1 - the gateway's session routes included - has no client outside this repository until the admin app ships
  (PP3.8) and public sign-up opens (BIZ-009). Until that release it is pre-release: a breaking change is allowed when
  the OpenAPI document, the contract tests, and the decision log change in the same commit (PP1.12 used this for the
  confirmed-email 403s, the session id header, and the current-session check; PP12.3 will for the spam challenge's
  sign-up field). From that release on, the breaking-change rule below applies to it in full. The publishing API is
  never pre-release (API-009).
- `session-ended` is a 401 everywhere the admin app meets it. On the internal surface it is a 400 the gateway acts on
  (clearing its cookie and answering 401), because there it is a refusal of the gateway's request, not of a browser.
- Pagination is cursor-based with `limit` (max 100).
- Every write that can be retried accepts `Idempotency-Key`; a replay returns the stored result (PP1.9). A route opts in
  with `[Idempotent]` (optionally `Required = true`), and the host refuses to start if that is on a read or on a route
  without a signed-in caller: keys are scoped to the caller, and remembered for 24 hours. A key is 1 to 128 visible ASCII
  characters, bare or quoted. The answers it adds: 400 `idempotency-key-invalid` or `idempotency-key-required`, 409
  `idempotency-key-in-use` with `Retry-After` while the first request runs, and 422 `idempotency-key-reused` for the same
  key on a different request. A replay carries `Idempotent-Replayed: true` and only allow-listed headers, and an
  opted-in write runs to its end even when the caller goes away, so the retry of a lost answer gets it. A route whose
  answer carries a secret (an authenticator key, recovery codes, a publishing key shown once) is marked
  `[AnswersWithSecret]`, and the host refuses it `[Idempotent]`; so does a route that rotates the session
  (`[RefreshesSession]`) or sits on the internal surface.
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

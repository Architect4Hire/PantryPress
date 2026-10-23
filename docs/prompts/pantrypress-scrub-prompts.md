# PantryPress — SCRUB Microprompts

Atomic prompts for building PantryPress, a standalone hosted food-blogging platform, with Claude Code and the
PantryPress `.claude/` toolkit. Suggested repository home: `docs/prompts/pantrypress-scrub-prompts.md`.

The functional source of truth is `docs/requirements/pantrypress-requirements.md` (the "requirements").
The architectural source of truth is `CLAUDE.md`, `.claude/rules/`, `.claude/skills/`, and
`docs/architecture-decisions/pantrypress.md` once PP0.1 creates it. If they conflict, stop and reconcile the
documents before writing code.

PantryPress is its own repository and Aspire application. It shares **no code** with CreatorPantry. It
borrows CreatorPantry's delivery discipline: one seam per prompt, plan first, explicit restrictions,
checkpoints, and separate audit prompts.

**These are deliberately small.** The document has **21 phases (PP0–PP20) and 257 microprompts**. Run one
prompt at a time. A prompt may cross layers only when it is explicitly a vertical-integration or
verification prompt.

## The "easy as pie" product rule

PantryPress hides system complexity from bloggers. Every user-facing workflow must:

- begin with a plain-language goal such as "Write a recipe post" or "Launch my blog";
- show one obvious primary action per step and disclose advanced options only on request;
- save work automatically and survive refreshes, network failures, and another device;
- preview anything public before it goes live, and offer revision history or undo for content changes;
- never show DNS, caches, blobs, certificates, page data, or HTML unless the blogger opts into a custom
  domain, and then only the records they must copy;
- mean "it is on my blog now" when it says "Publish", and show the live URL, not a job ID;
- meet WCAG 2.2 AA without drag-only interactions.

## The reusable SCRUB skeleton

```text
SCOPE:        one observable change and the exact repository seam it touches
CONSTRAINT:   stack, rule files, skills, requirement IDs, and prerequisites
RESTRICTION:  explicit exclusions and invariants that must not be weakened
UTILIZATION:  skills, read-only reviewers, and tools to invoke
BEHAVIOR:     inspect, plan, wait for approval, implement, test, and report
```

## How to use this file

- Run the phases in order, one prompt at a time. Do not paste a whole phase into Claude Code.
- Start each prompt in a clean context when practical.
- Every authenticated route uses `add-endpoint`; every anonymous public-site route uses `add-public-endpoint`.
  Both follow `Controller → Facade → Business → DataLayer → Repository | Gateway`.
- Every site-owned entity uses `add-site-entity` and owes a **two-site test**: two sites with similar data
  cannot read, mutate, cache, or enumerate each other.
- Every recipe change uses `add-recipe-feature` and preserves the blogger's wording.
- Every editor block uses `add-editor-block`; every admin UI slice uses `add-admin-component`.
- Every theme or render change uses `add-theme-component` or `add-render-artifact` and updates golden files
  deliberately, never by blanket re-approval.
- Site design follows PP-D17 and `docs/requirements/pantrypress-customization-spec.md`: components read only
  snapshot tokens (`--L-*`, `--D-*`, shared tokens) and `data-*` attributes on the site root. No component
  reads settings directly, and no customization logic runs in the reader's browser.
- Public themes are Angular 22 SSR components rendered on every request and cached at the edge (PP-D02,
  PP-D08). They must be SSR-safe: no `window` or `document` access outside browser-only hooks, no data access
  except the route's page-data resolver, and no hydration outside `@defer (hydrate on …)` islands.
- Every external provider (Stripe, Front Door, email, malware scanning, Safe Browsing, spam challenge, ad
  networks) uses `add-external-integration`; provider DTOs never cross the gateway boundary.
- `public-site-reviewer` runs after every prompt that changes public output; `site-isolation-auditor` after
  every prompt that touches site-owned data.
- Plan first and wait for approval whenever the prompt says so.
- Mark a prompt `- done` only after its stated verification passes, and add a delivery note when what shipped
  differs from what was asked.

## Credentials and external prerequisites

| First needed | Requirement |
| --- | --- |
| PP0 | .NET 10 SDK, Node and npm, a Docker-compatible runtime, and the Aspire CLI |
| PP0–PP8 | Nothing else. Everything runs locally under `aspire run` with `{slug}.localhost` hosts. |
| PP6 | Malware scanning approach for local development (fake) and the target service for staging |
| PP9 | Azure subscription, `pantrypress.app` delegated to Azure DNS, and the shared Front Door Standard profile |
| PP12 | Spam challenge site key and secret (default: Cloudflare Turnstile) |
| PP13 | Azure Communication Services Email with a verified sending domain |
| PP14 | Stripe account in test mode, Stripe Tax, webhook signing secret |
| PP15 | AdSense test publisher ID; IAB TCF v2.2 consent platform account |
| PP17 | A disposable test domain |
| PP18 | Nothing new; CreatorPantry is optional and only needed for an end-to-end demo |
| PP19 | Production subscription, deployment identity, Google Safe Browsing key, status page host |
| PP20 | Sample WordPress WXR exports with WP Recipe Maker and Tasty Recipes data |

## Checkpoints

| After | Demonstrable result |
| --- | --- |
| **PP0.8** | `aspire run` shows SQL, Redis, blob, migration service, API, site API, gateway, worker, and both Angular apps healthy |
| **PP1.12** | A blogger can register, verify email, sign in through the BFF, refresh, and sign out without exposing a token |
| **PP2.14** | Two sites cannot see each other; `demo.localhost` resolves through the site API; members have the right powers |
| **PP3.10** | The `@pantrypress/ui` showcase and the admin shell render in light and dark themes and pass an accessibility smoke test |
| **PP4.16** | A blogger creates a structured recipe, pastes ingredients, scales and converts it deterministically, and sees valid JSON-LD |
| **PP5.18** | A blogger writes a post with a recipe card, autosaves, restores a revision, previews, schedules, and publishes |
| **PP6.10** | Images upload, scan, get alt text and focal points, and produce public derivatives without exposing originals |
| **PP7.14** | The published post renders through Angular SSR at `demo.localhost` with recipe card, print view, and scaling, with SQL stopped |
| **PP8.14** | Home, archives, taxonomy pages, site pages with sections and page blocks, sitemaps, feeds, redirects, and search rebuild after a publish |
| **PP9.10** | A staging site is live over HTTPS through Front Door, survives the API being stopped, and updates within 60 seconds |
| **PP10.10** | A new account completes "Launch my blog" and has a live post in under 30 minutes |
| **PP11.19** | A blogger restyles a site from a preset with the customizer, previews, applies, and reverts; every preset and the option combination matrix pass accessibility and performance gates |
| **PP12.12** | Readers rate and comment; the blogger moderates; spam is held; `AggregateRating` reflects only real ratings |
| **PP13.8** | Readers subscribe with double opt-in; the blogger exports a CSV with consent data; the contact form works |
| **PP14.13** | A trial converts through Stripe; entitlements gate features; downgrade and non-payment never lose content |
| **PP15.8** | AdSense, an allow-listed network, affiliate disclosure, and consent work under a per-site CSP |
| **PP16.7** | Cookie-free analytics work; GA4 loads only after consent |
| **PP17.8** | A blogger opts into a custom domain, verifies it, gets a certificate, and can remove it with no downtime |
| **PP18.12** | A client using a publishing key creates, publishes, updates, and unpublishes a recipe post idempotently |
| **PP19.16** | Production launch gate passes |
| **PP20.8** | A WordPress food blog imports with recipes, media, comments, redirects, and a per-item report |

---

# Build sequence

## PP0 — Repository and runtime foundation

### PP0.1 Decisions record - done

```text
SCOPE: Create docs/architecture-decisions/pantrypress.md recording PP-D01 through PP-D18 and the owner
decisions in requirements section 1.2. The requirements, customization spec, and prompts are already in
docs/ from the repo starter; confirm they match the latest versions. Do not write code.
CONSTRAINT: Requirements sections 1, 3, and 8.
RESTRICTION: Do NOT silently resolve a DECIDE item. Record options, impact, and the default later prompts assume.
BEHAVIOR: Show the decision table, wait for approval, then write the record and link it from
docs/architecture.md.
```

*Delivery note (2026-10-23):* docs/architecture.md did not exist and was created as an index. With owner
approval, the customization spec and one requirements line were also reconciled (Starter gating, stale v1
wording, file paths); see section 5 of the decisions record.

### PP0.2 Toolkit alignment - done

```text
SCOPE: The repository starts from the PantryPress repo starter, which already contains CLAUDE.md,
README.md, and the .claude/ toolkit (17 rules, 12 skills, 7 reviewer agents, hooks, settings). Check them
against the PP0.1 decisions record and fix only what the record changed, such as a DECIDE item resolved
differently from its default.
CONSTRAINT: Requirements section 6; PP0.1 decisions record; .claude/README.md toolkit map.
RESTRICTION: No rewrite. CLAUDE.md stays under 250 lines and each rule file under 150. No product code.
UTILIZATION: architecture-reviewer.
BEHAVIOR: List each mismatch with the decision it conflicts with, wait for approval, apply the edits, then
prove the hooks run: a sample secret and an AI SDK reference are both blocked.
```

*Delivery note (2026-10-23):* With owner approval, the alignment also took in two items from the
requirements rather than the record: `auth.md` now gates publishing on email verification (ACCT-001), and the
AI SDK hook was widened to more package names plus imports and model endpoints in `src/`. ADR 0012's and ADR
0008's consequence lines were updated to match. Prompt conflicts found along the way (PP18.1, PP2.5, PP0.3)
are listed in section 5 of the decisions record.

### PP0.3 Solution skeleton - done

```text
SCOPE: Create the .NET 10 solution with the PP-D01 projects (AppHost, ServiceDefaults, Gateway, ApiService,
Site, Domain, Worker, MigrationService, Tests) and only the references they need.
CONSTRAINT: PP-D01; aspire.md and backend.md.
RESTRICTION: No entities, endpoints, auth, or feature code. Domain references no host project.
BEHAVIOR: Show commands and the reference graph, wait for approval, execute, and confirm the build succeeds.
```

*Delivery note (2026-10-23):* With owner approval, the solution is `PantryPress.slnx` (CLAUDE.md and
`format-guard.mjs` updated) and tests use xUnit. The AppHost registers the five executables with no
`WaitFor` yet (PP0.4, PP0.6). The AppHost SDK is 13.6.3 rather than the template's 13.6.1, because 13.6.1
pulls MessagePack 2.5.302 (GHSA-qhrr-8q5h-9q3h), which fails the build under warnings as errors. Shared build
settings live in `Directory.Build.props`; `global.json` pins SDK 10.0.402 with `latestFeature` roll-forward.

### PP0.4 SQL, Redis, and blob resources - done

```text
SCOPE: Declare PantryPress's own SQL Server database, Redis, and blob storage in the AppHost with secret
parameters, persistent lifetimes, and named volumes.
CONSTRAINT: PP-D16; aspire.md; add-aspire-resource.
RESTRICTION: No literal passwords, ports, or connection strings. Nothing shared with CreatorPantry.
UTILIZATION: add-aspire-resource.
BEHAVIOR: Plan names and persistence, wait for approval, implement, and show each resource healthy.
```

*Delivery note (2026-10-23):* With owner approval, the resources are `sql` with database `pantrypress`,
`redis`, and `storage` (Azurite locally) with `blobs`, on volumes `pantrypress-sql-data`,
`pantrypress-redis-data`, and `pantrypress-azurite-data`. `sql-password` and `redis-password` are generated
secret parameters persisted to the AppHost's user-secrets. No project references or `WaitFor` yet; they arrive
with their consumers (PP0.6). The AppHost pins `System.Security.Cryptography.Pkcs` 10.0.13 because
`Microsoft.Data.SqlClient` pulls 10.0.12 (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx). An `Aspire.Hosting.Testing`
smoke test (`Category=Integration`, needs Docker) runs the containers ephemeral; model tests need no Docker.

### PP0.5 ServiceDefaults, health, and application clock - done

```text
SCOPE: Wire ServiceDefaults into every executable with /health and /alive, and add one injectable clock and
time-zone service for domain code, jobs, scheduling, and audit.
CONSTRAINT: aspire.md and backend.md.
RESTRICTION: Liveness never calls SQL, Redis, blob, or providers. No DateTime.UtcNow in feature code.
BEHAVIOR: Implement with fixed-clock and DST boundary tests and report per-process health behavior.
```

*Delivery note (2026-10-23):* ApiService, Site, Gateway, and Worker map `/health` and `/alive`, which short-circuit
after routing; the AppHost probes `/health` on all four. The Worker moved to the Web SDK so it can answer probes and
serves nothing else. MigrationService registers ServiceDefaults but exposes no endpoints, because it is one-shot and
its exit code is its health signal (from PP0.6). Startup fails if any health check other than `self` carries the
`live` tag. The endpoints stay Development-only, as in the template; production probes on internal ingress are a
recorded PP19.2 default. `IClock` and `ITimeZoneService` live in `Domain/Managers/Time` (Gateway does not register
them). Time zones are IANA ids from NodaTime's embedded database, kept internal to `TzdbTimeZoneService`. A skipped
local time moves forward by the gap and a repeated one takes its first occurrence, recorded as a PP5.16 default.
`BannedSymbols.txt` (`Microsoft.CodeAnalysis.BannedApiAnalyzers`, every project) makes system clock and server
time zone APIs build errors; `TimeProvider` stays injectable for timers only.

### PP0.6 Migration service and DbContext - done

```text
SCOPE: Add PantryPressDbContext (empty model) and MigrationService as a one-shot worker that applies
migrations through an execution strategy, with WaitFor(sql) in the AppHost.
CONSTRAINT: backend.md and aspire.md.
RESTRICTION: Host apps never apply migrations at startup.
BEHAVIOR: Show loop and failure behavior, wait for approval, implement, and prove an empty run exits cleanly.
```

*Delivery note (2026-10-23):* With owner approval, `PantryPressDbContext` lives in `Domain/Managers/Persistence`
with no provider, and migrations compile into `PantryPress.MigrationService`, so no other host can find them. The
migration service waits on the `pantrypress` database rather than the bare `sql` server; that wait includes the
server. ApiService, Site, Worker, and Gateway `WaitForCompletion` on it with exit code 0. The exit code defaults to
failure and becomes 0 only after `MigrateAsync` succeeds inside the retrying execution strategy. Transient errors
retry, other errors fail at once, and a stop mid-run exits 1. `BannedSymbols.txt` makes `Migrate`,
`EnsureCreated`, `EnsureDeleted`, and `CreateTables` build errors everywhere except one suppression in the worker.
A design-time factory with no connection string and a pinned `dotnet-ef` (root `dotnet-tools.json`, the .NET 10
default location) support PP1.1. `dotnet ef database update --connection …` is unsupported; only the migration
service applies migrations. The EF SQL Server provider is pinned to 10.0.13 to match Domain, because Aspire 13.6.3
brings 10.0.12. Known limitation for PP19: `Microsoft.EntityFrameworkCore.Design` copies Roslyn build hosts into
the migration service output; trim them in the container step.

### PP0.7 Angular 22 workspace - done

```text
SCOPE: Scaffold the src/web Angular 22 workspace with three projects: pantrypress-ui (library),
pantrypress-admin (application), and pantrypress-site (application, @angular/ssr), strict TypeScript,
standalone components, zoneless change detection, and the admin dev server wired into the AppHost.
CONSTRAINT: PP-D07, PP-D08; frontend.md.
RESTRICTION: No feature screens, no auth, no second CSS framework, no hard-coded service URLs.
BEHAVIOR: Plan dev and production paths, wait for approval, implement, and build all three projects.
```

*Delivery note (2026-10-23):* Angular CLI 22.2.4, plain CSS, selector prefixes `pp` (library), `ppa` (admin),
`pps` (site). Angular 22 is zoneless and OnPush by default; `strict`, `strictTemplates`, and `strictStandalone`
are set explicitly and ESLint (angular-eslint 22) enforces OnPush, standalone, and no `any`. `@pantrypress/ui`
has two entry points, `@pantrypress/ui` and `@pantrypress/ui/themes` (empty; filled from PP3.2 and PP7.4), which
apps resolve from source through `paths`; `ng build pantrypress-ui` packages both. ESLint bans crossing them by
package name and relative path, and `eslint.ssr-safety.js` bans `window`, `document`, web storage, timers,
`Math.random`, `Date.now`, and `new Date()` in the site and the themes entry point. The site uses
`RenderMode.Server` for every route (no prerendering), `withIncrementalHydration()` (ADR 0008), and a root of
only `<router-outlet />` so layouts own landmarks; `server.ts` keeps the generated Node app engine without the
example API block. With owner approval the dev path changed from a dev-server proxy to YARP: in local
Development the gateway is the entry point at `app.localhost` and a catch-all route (`Order` 1000, in
`appsettings.Development.json`) forwards to the `admin` resource (`AddJavaScriptApp`, `npm run start:admin --
--port {aspire port}`) through service discovery. The admin resource exists only in run mode in Development, so
publishing has none; it waits for migrations, and the gateway references it without waiting, so an admin compile
error never blocks the gateway. Aspire's installer runs `npm install` (not `npm ci`, which would wipe
`node_modules` on every run). `Yarp.ReverseProxy` 2.3.0 is pinned explicitly. `package.json` overrides
`@angular/build`'s exact `vite` 8.3.0 to 8.3.5 for GHSA-9jrq-w75r-8gcw (high, dev server); remove the override
when `@angular/build` ships 8.3.3 or later. Production: `ng build pantrypress-admin` emits static files for the
gateway to serve (PP1.5/PP19); `ng build pantrypress-site` emits `browser/` and `server/server.mjs` for the Node
server (AppHost PP7.2a, container PP19). Recorded for later prompts:

- **PP7.2a/PP9.3:** Angular SSR rejects any Host not in `security.allowedHosts` with a 400 (no client-side
  fallback). Development allows `localhost` and `*.localhost`; production is empty, so a production build
  serves nothing until host validation moves to Front Door plus `SiteHost` (`allowedHosts: ['*']` and
  `trustProxyHeaders` behind the `X-Azure-FDID` check, with two-site host-header tests). `NG_ALLOWED_HOSTS` was
  used only to smoke-test the production build.
- **PP7.2:** no `npm run budget` yet (CLAUDE.md lists it); Angular's default budgets are far above RND-007.
  Production critical-CSS inlining and event replay add inline scripts; decide `autoCsp` or
  `inlineCritical: false` with PP9.6.
- **PP7.2a/PP7.13a/PP19:** `server.ts` caches all static files for a year (including unhashed ones and
  `index.csr.html`) and falls through to Express's default 404 and error handler; `/health` and `/alive` on the
  SSR server would shadow blogger slugs; `lang` and favicon in `index.html` must come from page data (PP7.5, PP11).
- **PP1.5:** unmatched `/api/...` paths currently fall through to the SPA with 200; map `/api/{**rest}` to
  ApiService at a lower `Order` and test for a problem-details 404.
- **PP1.6/PP1.7:** the gateway runs on its `http` profile under Aspire; decide dev HTTPS before `__Host-`
  cookie tests, strip `Cookie` on the `admin-spa` route, and keep YARP's destination Host on that route (the
  Angular dev server rejects foreign hosts).
- `MigrationServiceTests` lists dependents by hand; a new resource can skip the migration wait unnoticed.

Verification: `npm run build` (all three), `npm run lint`, `npm test` (Vitest, 3 suites), `npm run
format:check`, `npm audit` (0), lint probes for every boundary and SSR rule, SSR smoke test (200 with
`ng-server-context="ssr"`; foreign host 400), `dotnet build`, `dotnet test` (73 passed, including integration
tests that reach the admin app through the gateway with `Host: app.localhost`), `dotnet format
--verify-no-changes`, and `aspire run` with `admin` and `gateway` healthy and the HMR websocket upgrading through
YARP. Reviewers: architecture-reviewer and public-site-reviewer; findings fixed or recorded above.

### PP0.8 First complete local run

```text
SCOPE: Run the empty system and repair only wiring that blocks startup.
CONSTRAINT: aspire.md.
RESTRICTION: No feature work.
UTILIZATION: architecture-reviewer.
BEHAVIOR: Give one health line per resource, list startup fixes with evidence, leave the build clean.
```

## PP1 — Identity, gateway, and browser boundary

### PP1.1 ApplicationUser and identity store

```text
SCOPE: Add ApplicationUser to PantryPressDbContext with ASP.NET Core Identity stores and the initial
identity migration.
CONSTRAINT: ACCT-001; auth.md.
RESTRICTION: No endpoints yet. Migration reviewed before it is applied.
BEHAVIOR: Plan, wait for approval, implement, apply through MigrationService, test.
```

### PP1.2 Registration and email verification

```text
SCOPE: Add registration and email verification through the request seam, with a development email sink.
CONSTRAINT: ACCT-001, ACCT-003; Template B.
RESTRICTION: Publishing is blocked until email is verified. Rate-limited. No enumeration of existing emails.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP1.3 Password management and two-factor

```text
SCOPE: Add password reset and change, and optional TOTP two-factor with recovery codes.
CONSTRAINT: ACCT-001; auth.md.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP1.4 PlatformAdmin role and policy

```text
SCOPE: Add the PlatformAdmin role, an authorization policy, and an audited operator-only assignment path.
CONSTRAINT: ACCT-005; auth.md.
RESTRICTION: No self-service path to PlatformAdmin.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP1.5 Gateway routes and API versioning

```text
SCOPE: Configure YARP in PantryPress.Gateway to route the admin SPA's API calls to ApiService, add the API
versioning convention, and OpenAPI with a reference UI in development.
CONSTRAINT: ACCT-002; gateway.md and api-contract.md.
BEHAVIOR: Plan, wait for approval, implement with routing tests.
```

### PP1.6 BFF session and cookies

```text
SCOPE: Implement BFF sign-in, sign-out, and session refresh with a __Host- prefixed, host-only, Secure,
HttpOnly, SameSite=Lax cookie.
CONSTRAINT: ACCT-002, PSEC-003, PP-D13; gateway.md and auth.md.
RESTRICTION: The browser never sees or stores a token.
BEHAVIOR: Plan, wait for approval, implement with cookie and cookie-tossing tests from a sibling subdomain.
```

### PP1.7 Header sanitization, CORS, and antiforgery

```text
SCOPE: Strip untrusted forwarding headers, set strict CORS, and require antiforgery on state-changing
requests through the gateway.
CONSTRAINT: ACCT-002; gateway.md.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP1.8 Edge hardening

```text
SCOPE: Add security response headers, body size limits, and trusted-proxy configuration to Gateway,
ApiService, and Site.
CONSTRAINT: PSEC-002 (admin side); backend.md.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP1.9 Idempotency foundation

```text
SCOPE: Add an idempotency-key store and middleware for unsafe requests that opt in, returning the stored
result on replay.
CONSTRAINT: POST-005, API-005; backend.md.
RESTRICTION: Keys are scoped to the caller and expire (DECIDE 24 hours).
BEHAVIOR: Plan, wait for approval, implement with replay and concurrency tests.
```

### PP1.10 Transactional outbox

```text
SCOPE: Add a transactional outbox and dispatcher in the Worker with at-least-once delivery and idempotent
handlers.
CONSTRAINT: backend.md; Template G.
BEHAVIOR: Plan, wait for approval, implement with crash and replay tests.
```

### PP1.11 Audit log

```text
SCOPE: Add an append-only audit log for security-relevant and operator actions, with actor, site, action,
target, and timestamp.
CONSTRAINT: ACCT-005, OPS-006; backend.md.
RESTRICTION: No personal data beyond what the action needs. No content bodies.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP1.12 Identity and boundary audit

```text
SCOPE: Audit PP1 against ACCT-001–005 and PSEC-003. Report only.
CONSTRAINT: Template L.
UTILIZATION: architecture-reviewer, api-contract-checker.
BEHAVIOR: Return findings and checkpoint evidence. Wait for approval before fixes.
```

## PP2 — Sites, membership, and tenancy

### PP2.1 Sites module skeleton

```text
SCOPE: Create Modules/Sites/ in PantryPress.Domain with Facade, Business, Data, and Managers folders and the
module registration, plus ModuleBoundaryTests for the domain.
CONSTRAINT: PP-D01; backend.md.
RESTRICTION: No entities yet.
BEHAVIOR: Plan, wait for approval, implement, run the boundary tests.
```

### PP2.2 Site entity

```text
SCOPE: Add Site per SITE-001 with a platform-wide unique slug index and a concurrency token.
CONSTRAINT: SITE-001; Template C.
RESTRICTION: Site is the tenant root, so it carries no SiteId of its own; document that exception.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP2.3 Site membership and roles

```text
SCOPE: Add SiteMember (SiteId, UserId, role Owner | Editor | Author, status) and membership policies.
CONSTRAINT: SITE-010, PP-D03; tenancy.md and auth.md.
RESTRICTION: Every site has at least one Owner; the last Owner cannot leave or be demoted.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP2.4 Site-owned contract and request context

```text
SCOPE: Add the ISiteOwned contract, a site request context, and route-based site resolution for admin routes
from the authenticated membership.
CONSTRAINT: PP-D03, ARCH-015; tenancy.md.
RESTRICTION: Never accept SiteId from a body, query string, or client header. Unknown and inaccessible sites
are indistinguishable (404).
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP2.5 Global site query filter and SiteId stamping

```text
SCOPE: Add the global query filter for ISiteOwned entities and a SaveChanges interceptor that stamps SiteId
from the request context and forbids changing it.
CONSTRAINT: ARCH-015; tenancy.md.
RESTRICTION: IgnoreQueryFilters is prohibited outside documented operator and erasure paths.
UTILIZATION: site-isolation-auditor.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP2.6 Site cache-key helper

```text
SCOPE: Add a cache-key helper so every site-scoped cache key begins with the site identifier.
CONSTRAINT: ARCH-015, PSEC-007; tenancy.md.
BEHAVIOR: Plan, wait for approval, implement with collision tests.
```

### PP2.7 Two-site test harness

```text
SCOPE: Add a reusable test harness that seeds two sites with similar data and asserts no read, write,
cache, or enumeration crosses between them.
CONSTRAINT: ARCH-015, PSEC-007.
BEHAVIOR: Plan, wait for approval, implement and apply it to Site and SiteMember.
```

### PP2.8 Reserved slugs and slug policy

```text
SCOPE: Implement the SITE-002 slug policy with a versioned reserved list stored as platform reference data.
CONSTRAINT: SITE-002; Template A.
RESTRICTION: ASCII only, case-insensitive comparison.
BEHAVIOR: Plan, wait for approval, implement with table-driven tests.
```

### PP2.9 SiteHost and canonical host

```text
SCOPE: Add SiteHost (SiteId, host, kind Subdomain | Custom | WwwVariant, IsCanonical, status) with a unique
host index and exactly one canonical host per site.
CONSTRAINT: SITE-005; Template C.
RESTRICTION: Hosts stored lower-case without port or trailing dot.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP2.10 Create, list, and read sites

```text
SCOPE: Add endpoints to create a site (with its subdomain SiteHost and the creator as Owner), list the
caller's sites, read one site, and check slug availability.
CONSTRAINT: SITE-002, SITE-003; Template B.
RESTRICTION: Plan limits arrive in PP14 through the entitlements facade. Availability never reveals the
holder of a taken slug.
BEHAVIOR: Plan, wait for approval, implement with two-site tests.
```

### PP2.11 Invitations and member management

```text
SCOPE: Add invite by email, accept, change role, and remove member endpoints.
CONSTRAINT: SITE-010; Template B.
RESTRICTION: Owner only. Invitations expire and are single-use.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP2.12 PantryPress.Site host and host resolution

```text
SCOPE: Configure PantryPress.Site as the anonymous public site API, add it to the AppHost, and resolve Host to
SiteId through a cached SiteHost lookup, with {slug}.localhost support in development.
CONSTRAINT: ARCH-003, SITE-005, SITE-009, PSEC-004; sites.md and public-endpoints.md; add-public-endpoint.
RESTRICTION: No Identity or BFF cookies on this host. Read the site only from Host. No SQL per request once
warm. Reject IP-literal hosts.
UTILIZATION: public-site-reviewer.
BEHAVIOR: Plan, wait for approval, implement with known, unknown, mixed-case, port, www, and two-site tests.
```

### PP2.13 Public site context and status

```text
SCOPE: Add GET site context on PantryPress.Site returning public identity and status with SITE-008 and SEO-007
semantics.
CONSTRAINT: SITE-008, SEO-007; add-public-endpoint.
RESTRICTION: No private settings or member data in the response.
BEHAVIOR: Plan, wait for approval, implement with a test per status.
```

### PP2.14 Tenancy audit

```text
SCOPE: Audit PP2 against SITE-001–005, 008–010, ARCH-003, ARCH-015, PSEC-004, and PSEC-007. Report only.
CONSTRAINT: Template L.
UTILIZATION: site-isolation-auditor, public-site-reviewer, architecture-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP3 — Design system and admin shell

### PP3.1 Design-system decisions

```text
SCOPE: Write src/web/PANTRYPRESS-DESIGN-SYSTEM.md: principles, token architecture, the two entry points
(@pantrypress/ui for admin, @pantrypress/ui/themes for public themes), accessibility rules, and naming.
CONSTRAINT: PP-D07; design-system.md.
RESTRICTION: Document only.
BEHAVIOR: Show the draft, wait for approval, write.
```

### PP3.2 Token foundation

```text
SCOPE: Implement the shared token foundation (color, type, space, radius, elevation, motion) with light and
dark modes, as CSS custom properties exported from @pantrypress/ui.
CONSTRAINT: PP3.1; design-system.md.
RESTRICTION: Tokens are the only place colors live. Self-hosted fonts only.
BEHAVIOR: Plan, wait for approval, implement with a contrast test.
```

### PP3.3 Admin primitives: actions and inputs

```text
SCOPE: Add admin primitives: button, icon button, text field, textarea, select, checkbox, radio group,
switch, and field wrapper with label, hint, and error.
CONSTRAINT: add-admin-component; design-system.md.
BEHAVIOR: Plan, wait for approval, implement with unit and axe tests.
```

### PP3.4 Admin primitives: layout and feedback

```text
SCOPE: Add card, page header, tabs, toolbar, dialog, drawer, toast region, notice, empty state, progress, and
status pill.
CONSTRAINT: add-admin-component.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP3.5 Admin primitives: data

```text
SCOPE: Add list shell with paging and sorting, search field, chip group, and an uploader shell.
CONSTRAINT: add-admin-component.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP3.6 Showcase

```text
SCOPE: Add a showcase route that renders every primitive in every state and both modes.
CONSTRAINT: design-system.md.
BEHAVIOR: Plan, wait for approval, implement with an accessibility smoke test.
```

### PP3.7 Runtime configuration and typed API base

```text
SCOPE: Add runtime configuration and a typed API base service in pantrypress-admin.
CONSTRAINT: frontend.md.
RESTRICTION: Components never inject HttpClient.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP3.8 BFF auth client and guards

```text
SCOPE: Add the BFF session client, route guards, and sign-in, sign-up, and verify-email screens.
CONSTRAINT: ACCT-001, ACCT-002; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP3.9 Admin shell and site switcher

```text
SCOPE: Add the admin shell (navigation, header, account menu) and a site switcher that sets the route's site.
CONSTRAINT: SITE-010; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP3.10 Front-end foundation audit

```text
SCOPE: Audit PP3 for design-system compliance and accessibility. Report only.
CONSTRAINT: Template L.
UTILIZATION: design-review.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP4 — Recipes and calculations

### PP4.1 Reference vocabulary

```text
SCOPE: Add platform reference data for units (with aliases and systems), courses, cuisines, diets, and
allergens, plus a seed set.
CONSTRAINT: PP-D06; recipes.md.
RESTRICTION: Reference data has no SiteId and is read-only to bloggers.
BEHAVIOR: Plan, wait for approval, implement with seed tests.
```

### PP4.2 Quantity value objects

```text
SCOPE: Add Quantity (exact rationals, fractions, ranges, "to taste") and display formatting.
CONSTRAINT: RCP-002, RCP-004; recipes.md.
RESTRICTION: No floating-point arithmetic for quantities.
BEHAVIOR: Plan, wait for approval, implement with property-based tests.
```

### PP4.3 Recipes module and recipe entity

```text
SCOPE: Create Modules/Recipes/ and add Recipe with ingredient groups and lines, step groups and steps,
equipment, notes, times, yield, taxonomy, keywords, video, and optional nutrition per RCP-001.
CONSTRAINT: RCP-001, RCP-006; Template C; add-site-entity and add-recipe-feature.
RESTRICTION: Each ingredient line keeps the original text alongside parsed fields.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP4.4 Recipe revisions

```text
SCOPE: Add draft and published recipe revisions; published revisions are immutable.
CONSTRAINT: RCP-007; Template D.
BEHAVIOR: Plan, wait for approval, migrate, test immutability.
```

### PP4.5 Ingredient-line tokenizer and parser

```text
SCOPE: Implement a deterministic ingredient-line parser (quantity, unit, item, note) that flags unparseable
lines.
CONSTRAINT: RCP-002; recipes.md.
RESTRICTION: Never rewrites the original text. No AI.
BEHAVIOR: Plan, wait for approval, implement with a large fixture corpus.
```

### PP4.6 Paste-to-parse endpoint

```text
SCOPE: Add an endpoint that parses a pasted ingredient or step list into proposed structured lines without saving.
CONSTRAINT: RCP-003; Template B.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP4.7 Scaling service

```text
SCOPE: Implement deterministic scaling for servings multipliers with rounding and fraction display rules.
CONSTRAINT: RCP-004; recipes.md.
BEHAVIOR: Plan, wait for approval, implement with tests for ranges, fractions, and unscalable lines.
```

### PP4.8 Unit and temperature conversion

```text
SCOPE: Implement US and metric conversion for volume, weight, and temperature, with a small density table
used only when known.
CONSTRAINT: RCP-005; recipes.md.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP4.9 Time totals and nutrition arithmetic

```text
SCOPE: Compute total time and per-serving nutrition display from creator-entered values.
CONSTRAINT: RCP-001, RCP-006.
RESTRICTION: Never estimates nutrition.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP4.10 Recipe JSON-LD generator

```text
SCOPE: Implement deterministic schema.org Recipe JSON-LD from a published recipe revision, with a
missing-required-fields report.
CONSTRAINT: RCP-008; recipes.md.
RESTRICTION: No inferred facts, ratings, or nutrition.
BEHAVIOR: Verify the current schema vocabulary, plan, wait for approval, implement with golden tests.
```

### PP4.11 Recipe CRUD endpoints

```text
SCOPE: Add create, read, update draft, list, archive, and duplicate recipe endpoints.
CONSTRAINT: RCP-001, RCP-007; Template B.
BEHAVIOR: Plan, wait for approval, implement with two-site tests.
```

### PP4.12 Recipe API service and library screen

```text
SCOPE: Add the typed recipes API service and a recipe library screen with search and filters.
CONSTRAINT: Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP4.13 Recipe editor: details, times, and yield

```text
SCOPE: Add the recipe editor shell with autosave and the details, times, and yield sections.
CONSTRAINT: RCP-001, PNFR-007; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP4.14 Recipe editor: ingredients and steps

```text
SCOPE: Add grouped ingredient and step editing with paste-to-parse review, keyboard reorder, and flags for
unparsed lines.
CONSTRAINT: RCP-002, RCP-003; Template H.
RESTRICTION: No drag-only reordering.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP4.15 Recipe editor: preview of scaling, units, and JSON-LD readiness

```text
SCOPE: Show scaled and converted previews and the JSON-LD missing-fields list in the editor.
CONSTRAINT: RCP-004, RCP-005, RCP-008; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP4.16 Recipe audit

```text
SCOPE: Audit PP4 against RCP-001–009. Report only.
CONSTRAINT: Template L.
UTILIZATION: site-isolation-auditor, test-gap-analyzer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP5 — Posts, editor, and publishing

### PP5.1 Editor library spike

```text
SCOPE: Evaluate the editor library (default TipTap/ProseMirror via an Angular wrapper) against POST-001/002:
schema control, custom nodes, accessibility, bundle size, and serialization. Report only.
CONSTRAINT: POST-001; frontend.md.
BEHAVIOR: Verify current versions, build a throwaway proof, and recommend. Stop for the owner's decision.
```

### PP5.2 Block document schema

```text
SCOPE: Define the versioned block document schema (.NET model and TypeScript types) with the POST-002 blocks
and marks, and a server-side validator.
CONSTRAINT: POST-001, POST-002; content.md.
RESTRICTION: Unknown blocks and marks are rejected. Links are http, https, or mailto only.
BEHAVIOR: Plan, wait for approval, implement with validation tests.
```

### PP5.3 Posts module and post entity

```text
SCOPE: Create Modules/Posts/ and add Post with POST-004 fields and status Draft | Scheduled | Published |
Unpublished.
CONSTRAINT: POST-004; Template C.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP5.4 Post revisions and autosave

```text
SCOPE: Add draft revisions with autosave coalescing, revision history, compare, and restore.
CONSTRAINT: POST-003, PNFR-007; Template C.
RESTRICTION: Restore creates a new draft revision; history is never rewritten.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP5.5 Categories and tags

```text
SCOPE: Add site categories (seeded from course, cuisine, diet, season) and tags with stable slugs, plus CRUD.
CONSTRAINT: POST-004; Template C and B.
BEHAVIOR: Plan, wait for approval, implement with two-site tests.
```

### PP5.6 Post CRUD endpoints

```text
SCOPE: Add create, read, save draft, list, and delete-draft post endpoints with role checks.
CONSTRAINT: POST-004, SITE-010; Template B.
RESTRICTION: Authors see and edit only their own posts.
BEHAVIOR: Plan, wait for approval, implement with role and two-site tests.
```

### PP5.7 Editor component

```text
SCOPE: Add the block editor component in pantrypress-admin with the POST-002 schema, toolbar, and
keyboard shortcuts.
CONSTRAINT: POST-001, POST-002; add-editor-block; Template H.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP5.8 Image and gallery blocks

```text
SCOPE: Add image and gallery blocks that pick from the media library (stubbed until PP6) with alt text.
CONSTRAINT: POST-002, MED-003; add-editor-block.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP5.9 Callout, quote, list, divider, and video blocks

```text
SCOPE: Add tip and note callouts, quote, list, divider, and allow-listed video embed blocks.
CONSTRAINT: POST-002; add-editor-block.
RESTRICTION: Video embeds only from the allow-list; stored as provider and ID, never raw HTML.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP5.10 Recipe card block

```text
SCOPE: Add the recipe card block that references a recipe, with create-new and pick-existing flows.
CONSTRAINT: RCP-009, POST-002; add-editor-block.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP5.11 Post editor screen

```text
SCOPE: Add the post editor screen: title, editor, sidebar for slug, excerpt, images, taxonomy, author, and
SEO, with autosave status and an unsaved-changes guard.
CONSTRAINT: POST-003, POST-004, PNFR-007; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP5.12 Revision history UI

```text
SCOPE: Add revision history with compare and restore.
CONSTRAINT: POST-003; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP5.13 Publish readiness check

```text
SCOPE: Implement the POST-006 readiness check as a deterministic domain service and endpoint.
CONSTRAINT: POST-006.
BEHAVIOR: Plan, wait for approval, implement with a test per item.
```

### PP5.14 Publish, unpublish, and published revisions

```text
SCOPE: Implement publish (immutable published revision of the post and referenced recipes), unpublish, and
republish, raising an outbox event for page-data builds.
CONSTRAINT: POST-005, PP-D04; Template D.
RESTRICTION: Idempotent. Never deletes published revisions. Status becomes live only after the page-data
build confirms (PP7).
BEHAVIOR: Plan, wait for approval, implement with replay and two-site tests.
```

### PP5.15 Slug changes and automatic redirects

```text
SCOPE: Create a redirect record automatically when a published post's slug changes, and track "Updated on" for
material republishes.
CONSTRAINT: POST-007; Template A.
RESTRICTION: This is the minimal redirect table that PP8.9 extends.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP5.16 Scheduling

```text
SCOPE: Add scheduled publishing in the site time zone through a Worker job.
CONSTRAINT: POST-005; Template G.
RESTRICTION: No guessed time zone. Cancel is idempotent.
BEHAVIOR: Plan, wait for approval, implement with DST and replay tests.
```

### PP5.17 Review workflow

```text
SCOPE: Add the optional "Authors submit for review" policy with submit, approve, and return actions.
CONSTRAINT: POST-010, SITE-010.
BEHAVIOR: Plan, wait for approval, implement with role tests.
```

### PP5.18 Publish panel and authoring audit

```text
SCOPE: Add the publish panel (readiness list, publish now, schedule, live URL) and then audit PP5 against
POST-001–010 and BIZ-001. Report the audit only.
CONSTRAINT: Template H for the panel; Template L for the audit.
UTILIZATION: design-review, site-isolation-auditor.
BEHAVIOR: Plan the panel, wait for approval, implement, then return audit findings and checkpoint evidence.
```

## PP6 — Media library

### PP6.1 Media module and asset entity

```text
SCOPE: Create Modules/Media/ and add MediaAsset (SiteId, original blob pointer, hash, type, dimensions,
alt text, caption, credit, focal point, status).
CONSTRAINT: MED-001, MED-003; Template C; add-media-feature.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP6.2 Upload to staging

```text
SCOPE: Add direct upload to a private staging container through the BFF with type sniffing and size limits.
CONSTRAINT: MED-001; add-media-feature.
RESTRICTION: Originals are never publicly reachable.
BEHAVIOR: Plan, wait for approval, implement with malicious-file tests.
```

### PP6.3 Malware scanning gate

```text
SCOPE: Scan staged files (Defender for Storage malware scanning or an approved alternative) and accept only
clean files.
CONSTRAINT: MED-001, PSEC-006; Template F.
RESTRICTION: Fail closed. Fake scanner in default CI.
BEHAVIOR: Verify the current service contract, plan, wait for approval, implement.
```

### PP6.4 Normalization and HEIC conversion

```text
SCOPE: Normalize accepted images (orientation, color profile, HEIC to JPEG) and strip EXIF location data.
CONSTRAINT: MED-001, MED-002; Template G.
BEHAVIOR: Verify the image library's current support, plan, wait for approval, implement.
```

### PP6.5 Public derivative specification and generation

```text
SCOPE: Generate public derivatives (AVIF and WebP at responsive widths, JPEG fallback, 1000×1500 pinnable image
respecting the focal point) into media-public/{siteId}/{assetHash}/{variant}.
CONSTRAINT: MED-002, RND-006; Template G.
RESTRICTION: Content-addressed names; idempotent.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP6.6 Media endpoints

```text
SCOPE: Add list, read, update metadata, and delete endpoints for media.
CONSTRAINT: MED-003, MED-004; Template B.
RESTRICTION: Deleting an in-use asset is refused with the list of uses.
BEHAVIOR: Plan, wait for approval, implement with two-site tests.
```

### PP6.7 Usage tracking and cleanup

```text
SCOPE: Track asset usage from post and recipe revisions, and clean up public derivatives no published revision
uses after a grace period.
CONSTRAINT: MED-004; Template G.
RESTRICTION: Dry-run mode first; never delete within the grace period (DECIDE 7 days).
BEHAVIOR: Plan, wait for approval, implement with race tests against a concurrent publish.
```

### PP6.8 Storage quota

```text
SCOPE: Meter storage per site and refuse uploads over the quota (configuration constant until PP14).
CONSTRAINT: MED-005, BILL-003.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP6.9 Media library UI and picker

```text
SCOPE: Add the media library screen, upload with progress, metadata editing with focal point, and the picker
used by editor blocks.
CONSTRAINT: MED-001–004; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP6.10 Media audit

```text
SCOPE: Audit PP6 against MED-001–005 and PSEC-006. Report only.
CONSTRAINT: Template L.
UTILIZATION: site-isolation-auditor, public-site-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```


## PP7 — Renderer and first theme

### PP7.1 Rendering module and page-data identity

```text
SCOPE: Create Modules/Rendering/ in PantryPress.Domain with a PageDataVersion identity (published revision IDs, settings version,
page-data schema version) and its hash.
CONSTRAINT: ARCH-005, ARCH-006; rendering.md.
RESTRICTION: No builder or templates yet.
BEHAVIOR: Plan, wait for approval, implement with hash stability tests.
```

### PP7.2 Angular SSR site application and budget spike

```text
SCOPE: Build out projects/pantrypress-site (scaffolded in PP0.7) as a zoneless Angular 22 application served
per request by @angular/ssr's Node app engine, with one hello-world route whose resolver loads page data from
a fake source, and one @defer (hydrate on interaction) island. Measure the compressed JavaScript and CSS it
ships and the SSR render time.
CONSTRAINT: PP-D02, PP-D08, RND-007, ARCH-005; frontend.md and rendering.md.
RESTRICTION: Do not touch the admin application. The route resolver is the only data access; components never
inject HttpClient. Use only @pantrypress/ui/themes styles; never import admin components.
BEHAVIOR: Verify the current Angular 22 SSR, zoneless, and incremental hydration APIs, plan, wait for
approval, implement, and report the measured budget against RND-007 with a recommendation on the PP-D08
fallback. Stop for the owner's decision if the budget does not fit.
```

### PP7.2a SSR server in the AppHost

```text
SCOPE: Add pantrypress-site (the Node SSR server built from projects/pantrypress-site) to the AppHost as a
JavaScript app with a reference to PantryPress.Site, health checks, OpenTelemetry, and local *.localhost
routing so {slug}.localhost reaches it.
CONSTRAINT: PP-D08, SITE-009; aspire.md; add-aspire-resource skill.
RESTRICTION: Node holds no secrets beyond the internal service token and has no SQL, Redis, or blob access.
It forwards the original Host to the site API and never chooses a site itself.
UTILIZATION: add-aspire-resource; aspire-monitoring.
BEHAVIOR: Verify the current Aspire JavaScript hosting API, plan, wait for approval, implement, and show it
healthy in the dashboard.
```

### PP7.2b Page-data contracts

```text
SCOPE: Define versioned page-data contracts per route type (post, print, home, category, tag, archive, page,
search, error) as .NET ServiceModels with generated TypeScript types for projects/pantrypress-site.
CONSTRAINT: PP-D02, ARCH-004, ARCH-005; api-contract.md.
RESTRICTION: Contracts carry presentation-ready, sanitized data only. No member, account, or draft data, no provider IDs, no
private fields. Type generation is scripted, not hand-copied.
UTILIZATION: api-contract-checker.
BEHAVIOR: Show the contracts with full examples, wait for approval, implement with round-trip tests and a
CI check that the generated types are current.
```

### PP7.3 HTML sanitizer

```text
SCOPE: Add an allow-list block-to-HTML renderer and sanitizer in .NET per RND-002, applied when page data is built, with a
documented tag, attribute, and URL scheme allow-list. SSR inserts only already-sanitized HTML.
CONSTRAINT: RND-002; rendering.md.
RESTRICTION: Use a maintained library; verify its current version. No inline handlers, javascript: or data:
URLs except data:image in an allow-list, no iframes except allow-listed video hosts (DECIDE).
BEHAVIOR: Plan, wait for approval, implement with an XSS corpus test suite.
```

### PP7.4 Theme token sets

```text
SCOPE: Add the @pantrypress/ui/themes entry point with the token contract from customization spec section 4:
light (--L-*) and dark (--D-*) color tokens, shared tokens (fonts, weights, base size, heading scale, radius,
image radius, measure), mode resolution for auto, light, and dark, and the Market preset's values as defaults.
CONSTRAINT: PP-D07, PP-D08, PP-D17, THM-003, THM-007; design-system.md; add-theme-component skill.
RESTRICTION: No CSS framework. No remote fonts. Tokens are the only place colors live. No per-theme component
forks: later presets are values, not code. SSR-safe only.
BEHAVIOR: Plan, wait for approval, implement with a contrast check test and a library build.
```

### PP7.4a Reader primitives

```text
SCOPE: Add SSR-safe reader primitives to @pantrypress/ui/themes: button, link, responsive image, card, tag chip,
pagination, form field, and notice, each themeable through tokens only.
CONSTRAINT: THM-002, THM-003, RND-006, RND-007; add-theme-component.
RESTRICTION: No hydration inside primitives. Never import admin-only components. Within the performance budget.
BEHAVIOR: Plan, wait for approval, implement with golden, axe, and budget tests.
```

### PP7.5 Base layout shell (Market preset)

```text
SCOPE: Implement the shared layout shell with the Market preset (split header, right sidebar): header,
navigation, main, footer with the "Made with PantryPress" badge slot, skip link, landmarks, and the site root
as a container-query container carrying data-* attributes.
CONSTRAINT: THM-001, THM-003, THM-007; add-theme-component skill.
RESTRICTION: Semantic HTML first. The shell never hydrates.
UTILIZATION: add-theme-component; public-site-reviewer.
BEHAVIOR: Plan, wait for approval, implement with golden and axe tests.
```

### PP7.6 Post page component

```text
SCOPE: Implement the post page: title, byline, dates, featured image, sections, jump-to-recipe button and
anchor, recipe card slot, and breadcrumb.
CONSTRAINT: RND-001, SEO-004; add-theme-component.
RESTRICTION: Ad slots, comments, and ratings are empty placeholders until their phases.
BEHAVIOR: Plan, wait for approval, implement with golden tests.
```

### PP7.7 Recipe card component

```text
SCOPE: Implement the recipe card: image, summary, times, yield, ingredients, steps, notes, equipment, and
author, using precomputed variants with a no-JS default of 1× and the site's unit system.
CONSTRAINT: RND-003, RND-004; recipes.md.
RESTRICTION: No client arithmetic. Nutrition and dietary labels render only per RND-004 with the disclaimer.
BEHAVIOR: Plan, wait for approval, implement with golden tests including no-nutrition and unscaled-line cases.
```

### PP7.8 Scaling, units, and cook mode enhancement

```text
SCOPE: Add the recipe-controls island: a @defer (hydrate on interaction) component that switches between
precomputed variants and enables cook mode via the Screen Wake Lock API when available.
CONSTRAINT: RND-003, ARCH-008, RND-007; rendering.md; add-theme-component.
RESTRICTION: Only this island hydrates. Browser APIs only after hydration. Page fully usable without
JavaScript. If PP7.2 chose the vanilla fallback, implement it as a script under 5 KB instead.
BEHAVIOR: Plan, wait for approval, implement with Playwright tests with JS on and off.
```

### PP7.9 Print view

```text
SCOPE: Render the recipe print page per RND-008 with an image toggle and the site URL footer.
CONSTRAINT: RND-008.
RESTRICTION: Print CSS only; no PDF generation.
BEHAVIOR: Plan, wait for approval, implement with golden and print-emulation tests.
```

### PP7.10 Structured data embedding

```text
SCOPE: Embed the PP4.10 Recipe JSON-LD plus Article, BreadcrumbList, Person, and WebSite graph in the post page.
CONSTRAINT: RND-005, BIZ-005; content.md.
RESTRICTION: No AggregateRating until PP12. No inferred facts.
BEHAVIOR: Plan, wait for approval, implement with schema validation tests.
```

### PP7.11 Page-data build job

```text
SCOPE: Add a Worker job that builds page data for every route affected by one published revision and writes it to a new
versioned blob prefix pp-pagedata/{siteId}/{version}/.
CONSTRAINT: ARCH-004, ARCH-006, ARCH-014; Template G; add-background-job skill.
RESTRICTION: Re-resolve the site before building. Duplicate delivery writes identical bytes. No pointer
switch in this prompt.
BEHAVIOR: Show the state machine, wait for approval, implement with replay, restart, and isolation tests.
```

### PP7.12 Atomic pointer switch, warm-up, and rollback

```text
SCOPE: Add the per-site live page-data pointer, switched atomically after a build completes, warm Redis with
the new version, mark the post live and record the live URL, and support rollback to a
previous version.
CONSTRAINT: ARCH-006, POST-009.
RESTRICTION: Readers never see a mix of versions. Keep the last N versions (DECIDE N=5) and purge older ones.
Edge purge is added in PP9.5; until then, local caches only.
BEHAVIOR: Plan, wait for approval, implement with concurrency tests.
```

### PP7.13 Page-data read endpoint

```text
SCOPE: Add the internal page-data endpoint in PantryPress.Site: given the forwarded Host and a path, resolve
the site, follow the live pointer, and return that route's page data from Redis, falling back to blob, with
ETag.
CONSTRAINT: ARCH-003, ARCH-007, ARCH-013, PNFR-002; add-public-endpoint.
RESTRICTION: Callable only by pantrypress-site with the internal service token; not routed by Front Door.
No SQL on the request once the host map is warm. Paths are normalized; traversal is impossible.
BEHAVIOR: Plan, wait for approval, implement with tests, including serving with SQL stopped.
```

### PP7.13a SSR request path

```text
SCOPE: In pantrypress-site, wire route resolvers to the page-data endpoint with an in-process LRU, set
Cache-Control for edge caching, and render the neutral 404 for unknown hosts and the SITE-008 and SEO-007
status pages (503 with Retry-After, 410).
CONSTRAINT: PP-D02, ARCH-003, ARCH-007, SITE-008, SEO-007; rendering.md.
RESTRICTION: One page-data fetch per request. Timeouts render a themed-neutral error, never a stack trace.
UTILIZATION: public-site-reviewer.
BEHAVIOR: Plan, wait for approval, implement with tests per status and with the site API stopped.
```

### PP7.14 Renderer audit and golden-file policy

```text
SCOPE: Audit PP7 against RND-001–008, ARCH-003–007, 012, 013, THM-001 (Market preset), and THM-007, and document the
golden-file update policy. Report only, except the policy document.
CONSTRAINT: Template L.
UTILIZATION: public-site-reviewer, architecture-reviewer, test-gap-analyzer.
BEHAVIOR: Return findings and PP7 checkpoint evidence. Wait for approval before fixes.
```

## PP8 — Site structure and SEO

### PP8.1 Taxonomy page-data model

```text
SCOPE: Define page data for category and tag listings from published revisions, with stable slugs and counts.
CONSTRAINT: POST-004, ARCH-014; add-render-artifact.
RESTRICTION: Built from published revisions only; drafts never appear.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP8.1a Section library

```text
SCOPE: Add the shared section library (customization spec section 13.3): section types, typed options and
limits, page-data shapes, and SSR components for featured recipe, latest posts, category row, recipe grid,
about strip, newsletter signup, and start-here links.
CONSTRAINT: PP-D18, SITE-011, THM-007; add-render-artifact and add-theme-component.
RESTRICTION: Sections read only their page-data slice; no per-request queries. Typed options only, no
per-section colors or fonts: sections inherit design settings. The newsletter section reuses the signup form
slot that PP13.5 fills.
BEHAVIOR: Plan, wait for approval, implement with golden and axe tests per section type.
```

### PP8.2 Home page

```text
SCOPE: Render the home page as the ordered list of homepage sections from site settings, using the PP8.1a
section library, with pagination for the latest-posts section.
CONSTRAINT: RND-001, SITE-011; add-render-artifact.
RESTRICTION: Derived artifact rebuilt after publish; no per-request queries.
BEHAVIOR: Plan, wait for approval, implement with golden tests.
```

### PP8.3 Category, tag, and archive pages

```text
SCOPE: Render paginated category, tag, and date archive pages.
CONSTRAINT: RND-001, ARCH-014; add-render-artifact.
RESTRICTION: Rebuild only affected pages after a publish.
BEHAVIOR: Plan, wait for approval, implement with tests for incremental rebuild.
```

### PP8.4 Site pages

```text
SCOPE: Add SitePage (About, Contact, Privacy, Disclosure, Accessibility, Terms, and a few custom pages) using the
PP5 block document and editor, with publish and unpublish, and page data for each page.
CONSTRAINT: SITE-007; Template C and B.
RESTRICTION: Recipe card blocks are not allowed on site pages. No raw HTML.
BEHAVIOR: Plan, wait for approval, implement with two-site tests.
```

### PP8.4a Section blocks on site pages

```text
SCOPE: Let site pages embed any PP8.1a section as a block, with the section's typed options edited in the
block's settings panel, and include section data in the page's page data.
CONSTRAINT: PP-D18, SITE-007, SITE-011, POST-002; add-editor-block.
RESTRICTION: Section blocks are allowed on site pages only, not posts. A page republishes its section data
when the source content changes, through the existing rebuild path.
BEHAVIOR: Plan, wait for approval, implement with editor, render, and rebuild tests.
```

### PP8.4b Page blocks: intro, FAQ, and social links

```text
SCOPE: Add the intro-with-photo, FAQ, and social-links page blocks for site pages, with optional FAQPage
JSON-LD per FAQ block.
CONSTRAINT: POST-011, MED-003, SITE-006; add-editor-block.
RESTRICTION: Site pages only. FAQ is capped at 20 pairs, plain text answers with links only. FAQPage JSON-LD
is off by default. Social links come from settings, not per-block URLs.
BEHAVIOR: Plan, wait for approval, implement with golden, axe, and Rich Results tests.
```

### PP8.5 Legal page templates

```text
SCOPE: Provide platform templates for Privacy, Disclosure, Accessibility, and Terms that prefill from site
settings and enabled integrations.
CONSTRAINT: SITE-007, CMP-004, CMP-006.
RESTRICTION: Templates are marked "review before publishing" and are not legal advice.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP8.5a About and Start here templates

```text
SCOPE: Provide About and Start here page templates (customization spec section 13.4) that prefill from site
settings with clearly marked placeholders and create draft pages.
CONSTRAINT: SITE-012, SITE-007; Template C.
RESTRICTION: Templates are starting content only; nothing is locked. Placeholders block publishing until
replaced (publish readiness check).
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP8.6 Navigation menus

```text
SCOPE: Render header and footer menus from settings with defaults derived from categories and pages.
CONSTRAINT: SITE-006; add-theme-component.
RESTRICTION: Max depth 2. Keyboard accessible without JavaScript.
BEHAVIOR: Plan, wait for approval, implement with axe tests.
```

### PP8.7 Meta, Open Graph, and Pinterest tags

```text
SCOPE: Render title, description, canonical, Open Graph, Twitter, and Pinterest rich-pin tags on every page.
CONSTRAINT: SEO-001.
RESTRICTION: Canonical always uses the site's canonical host.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP8.8 Sitemap, image sitemap, robots, and RSS

```text
SCOPE: Generate sitemap.xml (with image entries and a sitemap index when large), robots.txt, and RSS feeds per
site and per category after each publish.
CONSTRAINT: SEO-002; add-render-artifact.
RESTRICTION: Exclude unpublished, suspended, and trial-expired content. robots.txt disallows everything for
non-Live statuses.
BEHAVIOR: Plan, wait for approval, implement with validator tests.
```

### PP8.9 Redirect manager

```text
SCOPE: Extend the PP5.15 redirect table into a redirect manager: CRUD, bulk CSV import, loop and chain
detection, and serving 301s from the Site host.
CONSTRAINT: SEO-003; Template B; add-public-endpoint for serving.
RESTRICTION: Redirects are site-scoped. Only same-site or https absolute targets.
BEHAVIOR: Plan, wait for approval, implement with loop, chain, and isolation tests.
```

### PP8.10 Search index build

```text
SCOPE: Build a per-site search index file after each publish (title, summary, categories, ingredients).
CONSTRAINT: SEO-006; add-render-artifact.
RESTRICTION: Public content only. Size budget per site (DECIDE).
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP8.11 Search page and client

```text
SCOPE: Render a search page with a small client that queries the index file, plus a no-JS fallback that links
to categories.
CONSTRAINT: SEO-006, ARCH-008.
RESTRICTION: No server query endpoint.
BEHAVIOR: Plan, wait for approval, implement with Playwright tests.
```

### PP8.12 Verification fields and IndexNow

```text
SCOPE: Render Search Console and Pinterest verification meta tags from settings and submit changed URLs via
IndexNow after publish.
CONSTRAINT: SEO-005; Template F for IndexNow.
RESTRICTION: Verification values are validated tokens, not free HTML.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP8.13 404 and error pages per theme

```text
SCOPE: Render themed 404 and error pages per site with search and popular links.
CONSTRAINT: RND-001.
RESTRICTION: Unknown hosts still get the neutral platform 404, not a themed one.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP8.14 SEO audit

```text
SCOPE: Audit PP8 against SEO-001–007, SITE-006–007, and ARCH-014, including a Rich Results validation run on
sample posts. Report only.
CONSTRAINT: Template L.
UTILIZATION: public-site-reviewer, test-gap-analyzer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP9 — Edge delivery

### PP9.1 Shared platform module

```text
SCOPE: Write infra/platform IaC for the resources shared with CreatorPantry (PP-D16): the Front Door Standard
profile, the Container Apps environment with its VNet, and the Log Analytics and Application Insights
workspace, for the non-production environment. Document how CreatorPantry references them as existing.
CONSTRAINT: PP-D13, PP-D16, OPS-001; aspire-deployment skill.
RESTRICTION: Shared resources only: no SQL, Redis, storage, Key Vault, or DNS here. No production apply. No
credential literals.
UTILIZATION: aspire-deployment.
BEHAVIOR: Show the plan and cost, wait for approval, generate, validate with a what-if, and report.
```

### PP9.1a Azure DNS and wildcard record

```text
SCOPE: Write the IaC for PantryPress's own pantrypress.app Azure DNS zone, the wildcard CNAME to the Front Door
endpoint, and the app host record, for the non-production environment.
CONSTRAINT: PP-D13, SITE-003; aspire-deployment skill; OPS-001.
RESTRICTION: No production apply. No credential literals.
BEHAVIOR: Plan, wait for approval, generate, validate with a what-if, and report.
```

### PP9.2 Front Door endpoints, routes, and wildcard TLS

```text
SCOPE: Add IaC for PantryPress's endpoints and routes on the shared Front Door Standard profile: a wildcard
custom domain *.pantrypress.app (managed or Key Vault certificate per current support), app.pantrypress.app for
the admin gateway, two origin groups for blogs (pantrypress-site for page routes; PantryPress.Site for
/api/public/*, sitemaps, robots.txt, feeds, ads.txt, and the search index), a PantryPress WAF policy, and
Container Apps ingress IP restrictions allowing only the AzureFrontDoor.Backend service tag.
CONSTRAINT: PP-D13, PP-D16, PSEC-001; aspire-deployment.
RESTRICTION: Standard tier only (PP-D13); no Premium-only features such as Private Link or managed rule sets.
Never modify CreatorPantry's endpoints or routes. Origins accept traffic only from Front Door (service-tag IP
restriction plus X-Azure-FDID check). No production apply.
BEHAVIOR: Verify current Front Door wildcard certificate options, plan, wait for approval, generate and validate.
```

### PP9.3 Origin trust check

```text
SCOPE: In pantrypress-site and PantryPress.Site, reject requests that did not come through Front Door
(X-Azure-FDID match) outside local development, and trust X-Forwarded-Host only from Front Door. The SSR
server forwards that host to the page-data endpoint unchanged.
CONSTRAINT: PSEC-001, ARCH-003; edge hardening (PP1.8).
RESTRICTION: Host resolution must use the original host Front Door forwards, never a client-supplied header.
BEHAVIOR: Plan, wait for approval, implement with spoofing tests.
```

### PP9.4 Caching rules

```text
SCOPE: Define Cache-Control per response (SSR HTML with a long shared TTL and explicit purge, short browser
TTL; assets immutable; feeds and sitemaps short) and Front Door rule sets that honor them and vary only on
host. Resolve the PP-D02 DECIDE: verify whether Front Door can serve stale content on origin errors.
CONSTRAINT: PP-D02, PNFR-002, ARCH-007.
RESTRICTION: Dynamic island endpoints are never cached at the edge unless explicitly marked. Cache keys always
include host.
BEHAVIOR: Plan, wait for approval, implement with header tests and an IaC diff.
```

### PP9.5 Cache purge gateway

```text
SCOPE: Add a typed gateway for Front Door purge by host and path, and call it from the pointer switch for the
affected URLs.
CONSTRAINT: POST-009; Template F.
RESTRICTION: Purges are batched, idempotent, and never wildcard across hosts. No live call in default CI.
BEHAVIOR: Verify the current purge API, plan, wait for approval, implement with fake tests.
```

### PP9.6 Security headers and per-site CSP

```text
SCOPE: Generate HSTS, X-Content-Type-Options, Referrer-Policy, Permissions-Policy, and a per-site CSP from the
site's enabled integrations, served on every public response.
CONSTRAINT: PSEC-002, PP-D11; public-endpoints.md.
RESTRICTION: Default CSP allows no third-party origins. Integration prompts later add origins only through the
generator.
BEHAVIOR: Plan, wait for approval, implement with tests per integration combination.
```

### PP9.7 BFF cookie hardening

```text
SCOPE: Ensure the gateway session cookie is __Host- prefixed, host-only, Secure, HttpOnly, and SameSite Lax
or stricter, and that no PantryPress host can set or read it.
CONSTRAINT: PSEC-003, PP-D13; gateway.md and auth.md.
RESTRICTION: Do not break existing sign-in. Verify antiforgery still works.
BEHAVIOR: Plan, wait for approval, implement with cookie-tossing tests from a sibling subdomain.
```

### PP9.8 WAF custom rules and rate limits

```text
SCOPE: Add a Front Door Standard WAF policy with custom rules (rate limits per client IP, geo or IP block
lists, and blocks for obviously malicious paths), plus application rate limits on
public dynamic endpoints per IP, per site, and global.
CONSTRAINT: PSEC-001; existing rate limiting.
RESTRICTION: Rendered pages are never rate-limited at the origin in a way that breaks a viral spike served from cache.
BEHAVIOR: Plan, wait for approval, implement with tests and an IaC diff.
```

### PP9.9 Degraded-mode proof

```text
SCOPE: Prove ARCH-007: with SQL, ApiService, and the Worker stopped, SSR pages still render from page data;
with pantrypress-site or PantryPress.Site stopped, cached pages still load from the edge; and dynamic
islands show a polite degraded state.
CONSTRAINT: ARCH-007, PNFR-001.
RESTRICTION: Test and fix only what fails; no new features.
UTILIZATION: aspire-monitoring.
BEHAVIOR: Plan the fault-injection run, wait for approval, run it, fix, and report evidence.
```

### PP9.10 Edge audit

```text
SCOPE: Audit PP9 against ARCH-007, PSEC-001–003, PNFR-001–002, and POST-009, including publish-to-live timing
in staging. Report only.
CONSTRAINT: Template L.
UTILIZATION: public-site-reviewer, integration-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP10 — Admin experience and "Launch my blog"

### PP10.1 Site settings area

```text
SCOPE: Add the site settings area to pantrypress-admin: routes, a typed SiteSettingsApiService, and navigation.
CONSTRAINT: Template H; frontend.md.
RESTRICTION: No HttpClient in components.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP10.2 Site overview screen

```text
SCOPE: Site overview: live URL, status, recent posts, pending moderation count placeholder, and next
recommended action.
CONSTRAINT: BIZ-002; Template H.
RESTRICTION: All five data states.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP10.3 Site identity and author settings screen

```text
SCOPE: Edit name, tagline, logo, favicon, author profile, and social links with autosave.
CONSTRAINT: SITE-006, PNFR-007; Template H.
RESTRICTION: Saving rebuilds affected page data and purges the edge; the screen shows "updating your blog" progress.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP10.4 Settings update endpoint and refresh trigger

```text
SCOPE: Add PUT for site settings sections with concurrency, and enqueue a page-data rebuild and edge purge when render-relevant
settings change.
CONSTRAINT: SITE-006, ARCH-012; Template B.
RESTRICTION: One endpoint per settings section; no generic patch-anything endpoint.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP10.5 Navigation and homepage sections screen

```text
SCOPE: Edit menus and homepage sections with keyboard reorder (no drag-only): add a section from the PP8.1a
library, edit its typed options, hide, reorder, and preview.
CONSTRAINT: SITE-006, SITE-011; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP10.6 Preset picker with preview

```text
SCOPE: Pick a design preset and preview it against the creator's own latest posts before applying. PP11.17
later grows this screen into the full customizer.
CONSTRAINT: THM-004; Template H.
RESTRICTION: Preview is rendered by the SSR server from draft settings behind a signed, expiring preview
token; never cached at the edge and never indexable.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP10.7 Preview rendering endpoint

```text
SCOPE: Issue a signed, expiring preview token for a design-settings draft, and let the page-data endpoint
apply the draft settings when the SSR server presents a valid token.
CONSTRAINT: THM-004; Template B.
RESTRICTION: Preview responses are private, no-store, and noindex. A token works for one site only.
BEHAVIOR: Plan, wait for approval, implement with expiry and isolation tests.
```

### PP10.8 Slug rename

```text
SCOPE: Rename a site slug within the yearly limit, keep the old host redirecting for 12 months, and hold it
from reissue.
CONSTRAINT: SITE-004; Template B.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP10.9 "Launch my blog" flow

```text
SCOPE: Add the guided first-run flow: pick a name and subdomain, pick a theme, set identity, write or import a
first post (with a sample recipe option), publish, and see the live site. Theme choice means a preset. The
flow also creates About and Start here as drafts from the PP8.5a templates.
CONSTRAINT: BIZ-002; Template H.
RESTRICTION: Orchestrates existing facades only. Save, exit, and resume work at every step.
BEHAVIOR: Plan, wait for approval, implement with end-to-end tests.
```

### PP10.10 Admin experience audit

```text
SCOPE: Audit PP10 against BIZ-002, THM-004, SITE-004, and SITE-006, including a timed
new-account run. Report only.
CONSTRAINT: Template L.
UTILIZATION: design-review, api-contract-checker.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP11 — Site design and accessibility

This phase builds PP-D17. Read `docs/requirements/pantrypress-customization-spec.md` before each prompt; section
numbers below refer to it. Use the Studio mockup (`pantrypress-studio-mockup.html`) as the visual reference only.

### PP11.1 Design settings schema

```text
SCOPE: Add the versioned SiteThemeSettings document (spec section 3) as a typed record on the site, with enums in
kebab-case JSON, defaults, a validator for every range, enum, and text limit, and a migration.
CONSTRAINT: PP-D17, THM-002, THM-011, SITE-006; add-site-entity.
RESTRICTION: No free-form CSS, HTML, or URLs except the announcement link (https or site-relative). Invalid input is
rejected with a plain message, never silently coerced. No rendering changes in this prompt.
BEHAVIOR: Plan, wait for approval, implement with validation tests and a two-site test.
```

### PP11.2 Color engine

```text
SCOPE: Implement spec section 5 as pure domain functions: tones, WCAG luminance and ratio, mix, towards, inkOn,
derived light and dark tokens, rejection of unfixable colors with a suggested shade, and the contrast report.
CONSTRAINT: THM-005, THM-003.
RESTRICTION: Deterministic and dependency-free. No UI. Tone tables match the spec exactly.
BEHAVIOR: Plan, wait for approval, implement with unit tests, including 500 random brand colors per tone and the
#888888 rejection case.
```

### PP11.3 Curated font registry

```text
SCOPE: Add the self-hosted font registry from spec section 6.1: WOFF2 subsets, weights listed, @font-face
generation, fallback stacks, display weight and tracking per heading font, and preload rules.
CONSTRAINT: THM-006, RND-007.
RESTRICTION: Licenses permit self-hosting; record them. No Google Fonts calls at runtime. At most 4 font files per page.
The script face loads only when the sign-off or index card needs it.
BEHAVIOR: Plan, wait for approval, implement with a budget test.
```

### PP11.4 Theme snapshot builder and history

```text
SCOPE: Build the ThemeSnapshot (spec section 3.3) from settings on apply: tokens, attributes, fonts, sanitized text,
sections, contrast report, and settings hash. Keep the last 20 snapshots per site with restore.
CONSTRAINT: PP-D17, THM-004, THM-005, THM-007; Template B.
RESTRICTION: Snapshots are immutable. Apply and restore go through the outbox and trigger the existing edge purge.
BEHAVIOR: Plan, wait for approval, implement with tests and a two-site test.
```

### PP11.5 SSR snapshot injection

```text
SCOPE: Make the SSR site write the active snapshot onto the site root: shared and light tokens inline, dark tokens
in a generated style block with the auto media query, data-* attributes, and the dropcap class. Preview
renders the draft snapshot.
CONSTRAINT: THM-007, PP-D08, ARCH-012; add-render-artifact.
RESTRICTION: Snapshot comes from page data only. No customization JavaScript reaches the browser.
UTILIZATION: public-site-reviewer.
BEHAVIOR: Plan, wait for approval, implement with golden files for the Market defaults.
```

### PP11.6 Brand and logo options

```text
SCOPE: Render site name, tagline, monogram, the four mark shapes, logo replacement of the mark, and the generated
favicon fallback (spec section 8).
CONSTRAINT: THM-002, THM-011; add-theme-component.
RESTRICTION: Logo formats per the open decision (default raster only).
BEHAVIOR: Plan, wait for approval, implement with golden and axe tests.
```

### PP11.7 Type, shape, and surface options

```text
SCOPE: Apply heading and body fonts, body size, heading scale, drop cap, corner and photo radius, the four button
styles, and the five background textures (spec sections 6.2, 6.3, 9).
CONSTRAINT: THM-002, THM-003, THM-006; add-theme-component.
RESTRICTION: Textures sit only on the page background, never under text panels. Tokens only.
BEHAVIOR: Plan, wait for approval, implement with golden tests for each value and axe tests.
```

### PP11.8 Header styles and announcement bar

```text
SCOPE: Implement the five header styles and the announcement bar with optional link (spec section 10).
CONSTRAINT: THM-008, THM-011; add-theme-component.
RESTRICTION: Minimal collapses navigation into an accessible menu button at every width. No hydration except that button.
BEHAVIOR: Plan, wait for approval, implement with golden tests at 360, 600, 900, and 1280px and axe tests.
```

### PP11.9 Post layout: sidebar, width, and hero styles

```text
SCOPE: Implement sidebar right, left, and none; text width 600–860px; and the four hero styles, including the
title-overlay contrast check over the image focal area with fallback to split (spec section 11).
CONSTRAINT: THM-008, MED-003; add-theme-component.
RESTRICTION: Breakpoints are container queries on the site root. The overlay check runs at snapshot or page-data build
time, not in the browser.
BEHAVIOR: Plan, wait for approval, implement with golden, axe, and fallback tests.
```

### PP11.10 Recipe badges and chef note fields

```text
SCOPE: Add Recipe.badges (max 4, closed list) and Recipe.chefNote (max 280 characters) to the recipe model, API,
revisions, and recipe editor, and map diet badges into JSON-LD only when the diet field agrees.
CONSTRAINT: RCP-010, RCP-007, RCP-008; add-recipe-feature.
RESTRICTION: Badges are a closed enum. The note is plain text. No rendering in this prompt.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP11.11 Recipe card and step styles

```text
SCOPE: Render the classic, magazine, and index card styles, the three step styles, badges, the chef note, and the
custom jump-button label (spec section 12).
CONSTRAINT: THM-009, RND-003, RND-004; add-theme-component.
RESTRICTION: Every style keeps jump, print, scaling, units, cook mode, and the nutrition disclaimer. JSON-LD must be
byte-identical across styles.
BEHAVIOR: Plan, wait for approval, implement with golden tests per style, axe tests, and the JSON-LD identity test.
```

### PP11.12 Sections and order

```text
SCOPE: Render post modules after the recipe card in the saved order with visibility, plus the gallery,
inline-signup, ads, and related-row switches (spec section 13).
CONSTRAINT: THM-010, MON-002; add-theme-component.
RESTRICTION: Ratings and comments cannot be hidden on recipe posts. No ad slot ever renders inside the recipe card.
BEHAVIOR: Plan, wait for approval, implement with order, visibility, and ad-placement tests.
```

### PP11.13 Personal touches

```text
SCOPE: Render the handwritten sign-off and apply the newsletter heading and button text to all three signup forms
(spec section 14).
CONSTRAINT: THM-011, ENG-005; add-theme-component.
RESTRICTION: Plain text only, HTML-encoded. Forms keep working without JavaScript.
BEHAVIOR: Plan, wait for approval, implement with golden tests.
```

### PP11.14 Design presets

```text
SCOPE: Add the six presets from spec section 7.3 as data, apply and reset behavior, and presetId tracking (cleared
on any later edit).
CONSTRAINT: THM-001, PP-D17.
RESTRICTION: Presets are values over the shared component tree, never forks. Preset names stay DECIDE until confirmed.
BEHAVIOR: Plan, wait for approval, implement with a test that every preset validates and builds a snapshot.
```

### PP11.15 Customizer API

```text
SCOPE: Add endpoints to read settings, save a draft, validate with the contrast report, apply, discard, list
snapshots, and restore.
CONSTRAINT: THM-004, THM-005; Template B.
RESTRICTION: Owner and Editor roles only. Drafts never reach the edge. Idempotent apply.
UTILIZATION: api-contract-checker.
BEHAVIOR: Plan, wait for approval, implement with endpoint, role, and two-site tests.
```

### PP11.16 Customizer screen: groups and controls

```text
SCOPE: Build the customizer screen with the ten collapsible groups in spec order (Starting look, Brand and logo,
Color, Typography, Shape and surface, Header, Post layout, Recipe card, Sections and order, Personal
touches), the contrast badge, and keyboard reordering for sections.
CONSTRAINT: THM-002, THM-005; Template H.
RESTRICTION: Progressive disclosure: only the first two groups open by default. No drag-only interactions.
UTILIZATION: design-review.
BEHAVIOR: Plan, wait for approval, implement with component and axe tests.
```

### PP11.17 Customizer screen: live preview, apply, and revert

```text
SCOPE: Connect the PP10.6 preview to the customizer so each change re-renders the draft against the blogger's
latest post, then add Apply, Discard, and the snapshot history with Revert.
CONSTRAINT: THM-004; Template H.
RESTRICTION: Preview uses the PP10.7 signed token, is no-store and noindex, and never touches the live snapshot.
BEHAVIOR: Plan, wait for approval, implement with end-to-end tests.
```

### PP11.18 Combination test matrix, budgets, and theme release

```text
SCOPE: Automate spec section 18 (CUS-T01 to CUS-T15) in CI: overflow checks at four widths for every option value,
axe for every preset in both modes, the color property test, budget and font-count checks per preset, and
Lighthouse mobile on sample posts. When a build changes theme components, purge affected sites through the
existing throttled job.
CONSTRAINT: THM-003, THM-013, RND-007, PNFR-003, PNFR-005, ARCH-012.
RESTRICTION: The build fails on any budget or serious accessibility breach. Golden files change only deliberately.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP11.19 Site design audit

```text
SCOPE: Audit PP11 against PP-D17, THM-001–013, RCP-010, RND-007, and CMP-006, with a manual keyboard and
screen-reader pass on every preset. Report only.
CONSTRAINT: Template L.
RESTRICTION: No code changes.
UTILIZATION: public-site-reviewer, design-review.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP12 — Ratings and comments

### PP12.1 Audience module and Rating entity

```text
SCOPE: Create Modules/Audience/ in PantryPress.Domain and add site-owned Rating (SiteId, PublishedPostId, stars, optional review
text, display name, status, hashed fingerprint, timestamps).
CONSTRAINT: ENG-001, PP-D06; Template C.
RESTRICTION: No raw IP stored. Personal data columns encrypted per PSEC-005.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP12.2 Comment entity

```text
SCOPE: Add site-owned Comment with one level of replies, author name, email (encrypted), creator-reply
flag, and status pending | approved | spam | trash.
CONSTRAINT: ENG-002, PSEC-005; Template C.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP12.3 Spam challenge gateway

```text
SCOPE: Add a typed gateway for the spam challenge (default Cloudflare Turnstile) server-side verification.
CONSTRAINT: ENG-003; Template F.
RESTRICTION: Fail closed to "held for moderation", never to "approved".
BEHAVIOR: Verify the current API, plan, wait for approval, implement with fake tests.
```

### PP12.4 Spam heuristics

```text
SCOPE: Implement honeypot, minimum time-to-submit, link count, and per-IP and per-site rate rules as a
deterministic spam score.
CONSTRAINT: ENG-003.
BEHAVIOR: Plan, wait for approval, implement with table tests.
```

### PP12.5 Submit rating endpoint

```text
SCOPE: Add POST /api/public/ratings on the Site host: Host-derived site, post slug, stars, optional review.
CONSTRAINT: ENG-001, PSEC-004; add-public-endpoint.
RESTRICTION: No site identifier in the payload. Best-effort one rating per reader.
UTILIZATION: add-public-endpoint; public-site-reviewer.
BEHAVIOR: Plan, wait for approval, implement with spam, duplicate, and two-site tests.
```

### PP12.6 Submit comment endpoint

```text
SCOPE: Add POST /api/public/comments with first-time-commenter hold, spam scoring, and challenge verification.
CONSTRAINT: ENG-002, ENG-003; add-public-endpoint.
RESTRICTION: Comment text is plain text, rendered encoded; links become rel="nofollow ugc".
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP12.7 Dynamic islands for ratings and comments

```text
SCOPE: Serve approved comments and rating summaries as cached JSON fragments, rendered by @defer (hydrate on
viewport) Angular islands, with forms that work without JavaScript.
CONSTRAINT: ENG-008, ARCH-008.
RESTRICTION: Fragments are cached and invalidated on moderation; updates visible within 60 seconds.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP12.8 AggregateRating in structured data

```text
SCOPE: Add AggregateRating to the recipe JSON-LD from approved ratings only, refreshed in page data on a debounce with an edge purge.
CONSTRAINT: RND-005; rendering.md.
RESTRICTION: Omit it entirely when there are no ratings. Never seed or fabricate.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP12.9 Moderation endpoints

```text
SCOPE: Add authenticated list, approve, spam, trash, bulk action, and creator reply endpoints.
CONSTRAINT: ENG-004; Template B.
BEHAVIOR: Plan, wait for approval, implement with isolation tests.
```

### PP12.10 Moderation queue UI

```text
SCOPE: Moderation queue screen with filters, bulk actions, keyboard support, and inline reply.
CONSTRAINT: ENG-004; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP12.11 Moderation digest notification

```text
SCOPE: Send a daily in-app and email digest of pending comments using the existing notification seam.
CONSTRAINT: ENG-004; the PP13.1 email gateway and in-app notices.
RESTRICTION: Email sending waits for PP13.1; until then in-app only.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP12.12 Audience audit

```text
SCOPE: Audit PP12 against ENG-001–004, ENG-008, PSEC-004, PSEC-005, and PSEC-007. Report only.
CONSTRAINT: Template L.
UTILIZATION: public-site-reviewer, site-isolation-auditor.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP13 — Newsletter signup and contact

Newsletter **signup and CSV export only**. Composing, configuring, and sending newsletters is out of scope
(PP-D14). There is no sync to any email service.

### PP13.1 Transactional email gateway

```text
SCOPE: Add a typed gateway for Azure Communication Services Email for transactional mail only, with templates
for signup confirmation, contact form delivery, and moderation digest.
CONSTRAINT: PP-D14; Template F.
RESTRICTION: No bulk or broadcast capability is exposed. No live call in default CI.
BEHAVIOR: Verify the current API, plan, wait for approval, implement with fake tests.
```

### PP13.2 Subscriber entity

```text
SCOPE: Add site-owned Subscriber (SiteId, email encrypted, optional first name, status pending | confirmed
| unsubscribed, consent text, consent time, source page, confirmation token hash).
CONSTRAINT: ENG-005, CMP-003, PSEC-005; Template C.
RESTRICTION: Unique per site and email. No cross-site subscriber identity.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP13.3 Signup endpoint

```text
SCOPE: Add POST /api/public/subscribe: create or refresh a pending subscriber and send the confirmation email.
CONSTRAINT: ENG-005; add-public-endpoint.
RESTRICTION: Same response whether or not the email already exists. Rate-limited. Subscriber cap enforced
through the entitlements facade once PP14 lands (until then a configuration constant).
BEHAVIOR: Plan, wait for approval, implement with enumeration, rate-limit, and two-site tests.
```

### PP13.4 Confirm and unsubscribe endpoints

```text
SCOPE: Add confirm and unsubscribe endpoints using single-use tokens, with themed result pages.
CONSTRAINT: ENG-005, CMP-003.
RESTRICTION: Tokens are hashed at rest and expire (DECIDE 7 days).
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP13.5 Signup form components

```text
SCOPE: Add inline, end-of-post, and footer signup form components to every theme, working without JavaScript.
CONSTRAINT: ENG-005; add-theme-component.
BEHAVIOR: Plan, wait for approval, implement with golden and axe tests.
```

### PP13.6 Subscriber list and CSV export

```text
SCOPE: Add the subscriber list screen and a CSV export endpoint including consent metadata.
CONSTRAINT: ENG-006; Template B and H.
RESTRICTION: CSV is the only export path. Owner and Editor only. Export is audited. CSV cells are escaped
against formula injection.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP13.7 Contact form

```text
SCOPE: Add a contact form component and endpoint that emails the creator's verified address with the reader as
Reply-To.
CONSTRAINT: ENG-007; add-public-endpoint.
RESTRICTION: Spam controls from PP12.3–PP12.4. The creator's address is never rendered on the page.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP13.7a Contact page block and template

```text
SCOPE: Add the contact form as a page block (at most one per page) using the PP13.7 component and endpoint,
and the Contact page template (customization spec section 13.4) built around it.
CONSTRAINT: POST-011, SITE-012, ENG-007; add-editor-block.
RESTRICTION: Site pages only. Works without JavaScript. No address shown on the page.
BEHAVIOR: Plan, wait for approval, implement with editor, render, and spam-control tests.
```

### PP13.8 Signup and contact audit

```text
SCOPE: Audit PP13 against ENG-005–007, CMP-003, and PP-D14 (prove no sending or sync capability exists). Report only.
CONSTRAINT: Template L.
UTILIZATION: public-site-reviewer, integration-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP14 — Subscriptions and entitlements

### PP14.1 Billing module and plan catalogue

```text
SCOPE: Create Modules/Billing/ in PantryPress.Domain and a configuration-driven plan catalogue (Starter, Plus, Pro) with every limit
in requirements section 2.3 and BILL-003.
CONSTRAINT: BILL-003, PP-D09; billing.md.
RESTRICTION: Prices live in Stripe; limits live in configuration; neither is hard-coded in features.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP14.2 Entitlements facade

```text
SCOPE: Add IEntitlementsFacade answering "may this account or site do X, and what is the limit" for every
BILL-003 capability.
CONSTRAINT: ARCH-010; billing.md.
RESTRICTION: The only place a plan is interpreted.
BEHAVIOR: Plan, wait for approval, implement with tests per capability.
```

### PP14.3 Subscription and integration records

```text
SCOPE: Add platform-scoped AccountSubscription (account, plan, status, trial end, current period, grace state)
and the Stripe integration mapping table.
CONSTRAINT: BILL-002, ARCH-011; Template C variant for account scope.
RESTRICTION: Stripe IDs live only in the integration table.
BEHAVIOR: Defend the data zone, plan, wait for approval, migrate, test.
```

### PP14.4 Stripe gateway

```text
SCOPE: Add a typed Stripe gateway for customers, checkout sessions, customer portal sessions, and subscription reads.
CONSTRAINT: BILL-001, BILL-007; Template F.
RESTRICTION: No card data ever touches PantryPress. No live call in default CI.
BEHAVIOR: Verify the current Stripe API version, plan, wait for approval, implement with recorded tests.
```

### PP14.5 Webhook receiver

```text
SCOPE: Add the Stripe webhook endpoint: verify signature, store the event, de-duplicate by event ID, and enqueue
processing through the outbox.
CONSTRAINT: BILL-002; Template B.
RESTRICTION: The endpoint does no business work inline. Returns 2xx only after durable storage.
BEHAVIOR: Plan, wait for approval, implement with replay and bad-signature tests.
```

### PP14.6 Webhook processor and resync

```text
SCOPE: Process subscription events into AccountSubscription and add an on-demand resync from Stripe state.
CONSTRAINT: BILL-002; Template G.
RESTRICTION: Out-of-order events converge to Stripe's current state.
BEHAVIOR: Plan, wait for approval, implement with out-of-order and replay tests.
```

### PP14.7 Trial lifecycle

```text
SCOPE: Start a 14-day trial at sign-up, send reminders 3 days before and on the day, and move expired trial
sites to the "coming soon" state.
CONSTRAINT: BILL-006, PP-D10.
RESTRICTION: Content is retained. Uses the application clock.
BEHAVIOR: Plan, wait for approval, implement with fixed-clock tests.
```

### PP14.8 Checkout and portal endpoints

```text
SCOPE: Add endpoints that create Stripe Checkout and customer portal sessions for the signed-in account.
CONSTRAINT: BILL-001; Template B.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP14.9 Plans and billing UI

```text
SCOPE: Add the plans comparison, current plan, usage against limits, and "manage billing" screens.
CONSTRAINT: BILL-001; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP14.10 Enforce limits across features

```text
SCOPE: Wire the entitlements facade into site creation, design options (presets and Studio option gating
per THM-012), branding removal, subscriber cap, and storage cap.
CONSTRAINT: BILL-003, ARCH-010, THM-012.
RESTRICTION: One small change per feature, each with a test. Refusals explain the limit and the upgrade path.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP14.11 Downgrade and non-payment states

```text
SCOPE: Implement BILL-004 and BILL-005: over-limit items become read-only or hidden; non-payment moves sites to
ReadOnly, then Offline, with retention.
CONSTRAINT: BILL-004, BILL-005, SITE-008.
RESTRICTION: Never delete content. Every transition is audited and notified.
BEHAVIOR: Plan, wait for approval, implement with fixed-clock tests.
```

### PP14.12 Operator overrides

```text
SCOPE: Add PlatformAdmin endpoints to grant a comp plan, extend a trial, or override a limit, each audited.
CONSTRAINT: BILL-008; Template B.
BEHAVIOR: Plan, wait for approval, implement with authorization tests.
```

### PP14.13 Billing audit

```text
SCOPE: Audit PP14 against BILL-001–008, ARCH-010, and ARCH-011 with an end-to-end Stripe test-mode run.
Report only.
CONSTRAINT: Template L.
UTILIZATION: integration-reviewer, architecture-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP15 — Earning features

### PP15.1 Integration settings model

```text
SCOPE: Add typed integration settings: AdSense publisher ID, ad network (from the allow-list) and its site ID,
ads.txt lines, GA4 measurement ID, and affiliate domain list.
CONSTRAINT: PP-D11, MON-001, MON-002; Template A.
RESTRICTION: Every value is validated against its format. No free-form script or HTML.
BEHAVIOR: Plan, wait for approval, implement with validation tests.
```

### PP15.2 Consent banner and consent state

```text
SCOPE: Add the consent banner and a consent-state contract that gates every non-essential script, using an
IAB TCF v2.2 consent platform for EU and UK visitors when ads are on (vendor DECIDE).
CONSTRAINT: MON-004, CMP-002.
RESTRICTION: Nothing non-essential loads before consent. Works with the per-site CSP.
BEHAVIOR: Verify the vendor's current integration, plan, wait for approval, implement with tests.
```

### PP15.3 AdSense placement

```text
SCOPE: Render AdSense script and standard slots (in-content every N paragraphs, sidebar, below the recipe card)
for Plus and Pro sites, and add AdSense origins to the CSP.
CONSTRAINT: MON-001, MON-005; add-theme-component.
RESTRICTION: Never inside the ingredient or step lists. Slots reserve space to protect CLS.
BEHAVIOR: Plan, wait for approval, implement with golden and CLS tests.
```

### PP15.4 ads.txt

```text
SCOPE: Serve ads.txt per site from settings and the enabled network's required lines.
CONSTRAINT: MON-001, MON-002; add-render-artifact.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP15.5 Ad-network allow-list

```text
SCOPE: Add the first allow-listed ad network (DECIDE which) for Pro sites: script rendering, slot layout, and CSP origins.
CONSTRAINT: MON-002, PP-D11.
RESTRICTION: Adding a network later is a new prompt using this one as the pattern.
BEHAVIOR: Verify the network's current publisher documentation, plan, wait for approval, implement.
```

### PP15.6 Affiliate link handling

```text
SCOPE: Detect affiliate links (domain list plus creator flag) at render time, add rel="sponsored nofollow",
and insert the disclosure above the first one.
CONSTRAINT: MON-003, CMP-004; rendering.md.
BEHAVIOR: Plan, wait for approval, implement with golden tests.
```

### PP15.7 Earning settings UI

```text
SCOPE: Add the "Earn from my blog" settings screen, gated by plan, with plain-language guidance.
CONSTRAINT: MON-001–003, BILL-003; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP15.8 Earning audit

```text
SCOPE: Audit PP15 against MON-001–005 and CMP-004, including CSP per combination and CLS with ads on. Report only.
CONSTRAINT: Template L.
UTILIZATION: public-site-reviewer, integration-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP16 — Analytics

### PP16.1 Beacon endpoint

```text
SCOPE: Add POST /api/public/beacon recording page view, referrer domain, country, device class, and search term,
with a salted daily hash for uniqueness.
CONSTRAINT: ANL-001; add-public-endpoint.
RESTRICTION: No cookies, no IP stored, no fingerprinting beyond the daily hash. Accepts and drops on overload.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP16.2 Raw event buffer

```text
SCOPE: Buffer raw events (Redis stream or equivalent) and discard anything older than 48 hours.
CONSTRAINT: ANL-002; Template G.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP16.3 Daily aggregation job

```text
SCOPE: Aggregate raw events into daily per-site and per-page counts, retained per plan.
CONSTRAINT: ANL-002, BILL-003; Template G.
RESTRICTION: Idempotent re-aggregation for a day.
BEHAVIOR: Plan, wait for approval, implement with replay tests.
```

### PP16.4 Beacon script

```text
SCOPE: Add the beacon script (under 1 KB) using sendBeacon, respecting Do Not Track and Global Privacy Control.
CONSTRAINT: ANL-001, RND-007.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP16.5 Analytics read endpoints

```text
SCOPE: Add authenticated endpoints for views over time, top posts, referrers, search terms, rating and comment
activity, and subscriber growth.
CONSTRAINT: ANL-003; Template B.
BEHAVIOR: Plan, wait for approval, implement with isolation tests.
```

### PP16.6 Analytics dashboard UI

```text
SCOPE: Add the analytics dashboard with links from each post to its editor.
CONSTRAINT: ANL-003; Template H; dataviz guidance in the design system.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP16.7 GA4 option and analytics audit

```text
SCOPE: Load GA4 only after consent when configured, then audit PP16 against ANL-001–004. Report the audit only.
CONSTRAINT: ANL-004, MON-004; Template L for the audit.
UTILIZATION: public-site-reviewer.
BEHAVIOR: Plan GA4, wait for approval, implement, then return audit findings and checkpoint evidence.
```

## PP17 — Custom domains (opt-in)

### PP17.1 Custom domain entitlement and model

```text
SCOPE: Extend SiteHost for Custom hosts with verification token, verification status, certificate status,
and timestamps, gated by the entitlements facade.
CONSTRAINT: DOM-001, DOM-004; Template C.
RESTRICTION: Opt-in only. A host is unique platform-wide.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP17.2 Add domain endpoint and DNS instructions

```text
SCOPE: Add an endpoint to add a custom apex or www domain, returning the exact TXT and CNAME or alias records.
CONSTRAINT: DOM-001; Template B.
RESTRICTION: Reject pantrypress.app hosts, IP literals, and public-suffix-only names.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP17.3 Verification job

```text
SCOPE: Poll DNS for the TXT record with backoff and mark the domain verified.
CONSTRAINT: DOM-001; Template G.
BEHAVIOR: Plan, wait for approval, implement with fake resolver tests.
```

### PP17.4 Front Door custom domain gateway

```text
SCOPE: Add a typed gateway to create a Front Door custom domain with a managed certificate, associate it with
the route, and read its status.
CONSTRAINT: DOM-002; Template F.
RESTRICTION: No live call in default CI. Idempotent create.
BEHAVIOR: Verify the current API, plan, wait for approval, implement with fake tests.
```

### PP17.5 Activation and canonical switch

```text
SCOPE: When the certificate is active, make the custom domain canonical, 301 the subdomain to it, purge the
cache, and rebuild page data with the new canonical URLs.
CONSTRAINT: DOM-003, SITE-005.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP17.6 Removal, downgrade, and dangling detection

```text
SCOPE: Remove a domain on request or downgrade with automatic fallback to the subdomain, and detect domains whose
DNS no longer points at PantryPress.
CONSTRAINT: DOM-003, DOM-005, DOM-004.
RESTRICTION: No downtime: switch canonical first, then detach.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP17.7 Custom domain UI

```text
SCOPE: Add the "Use my own domain" screen: enter domain, copy records, live status, and remove.
CONSTRAINT: DOM-001–003; Template H.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP17.8 Custom domain audit

```text
SCOPE: Audit PP17 against DOM-001–005 with a real disposable domain in staging. Report only.
CONSTRAINT: Template L.
UTILIZATION: integration-reviewer, public-site-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```



## PP18 — Publishing API and CreatorPantry connection

PantryPress never calls CreatorPantry. CreatorPantry (or any trusted authoring tool) calls this API with a
per-site key. CreatorPantry's side of the work is listed in requirements section 7.3 and belongs in its own
repository.

### PP18.1 Publishing module and key entity

```text
SCOPE: Create Modules/Publishing/ and add PublishingKey (SiteId, name, scopes, key prefix, key hash, created
by, last used, revoked at).
CONSTRAINT: API-001, PSEC-008; Template C; publishing-api.md.
RESTRICTION: The full key is never stored or logged; store a prefix for display and a slow hash.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP18.2 Key management endpoints

```text
SCOPE: Add create (returns the key once), list, rename, and revoke endpoints for publishing keys.
CONSTRAINT: API-001; Template B.
RESTRICTION: Owner only. Every action is audited.
BEHAVIOR: Plan, wait for approval, implement with role and two-site tests.
```

### PP18.3 Key authentication and scopes

```text
SCOPE: Add an authentication handler for /api/publishing/v1 that resolves the key to its site and scopes and
sets the site request context, plus per-key and per-site rate limits.
CONSTRAINT: API-001, API-006, PP-D15, PSEC-008; publishing-api.md and tenancy.md.
RESTRICTION: The site comes only from the key. A revoked key fails immediately. Constant-time comparison.
BEHAVIOR: Plan, wait for approval, implement with revoked, wrong-scope, rate-limit, and two-site tests.
```

### PP18.4 PantryPress Publish Format v1

```text
SCOPE: Define the Publish Format v1 as JSON Schema plus .NET models: post fields, blocks (the POST-002 schema),
recipes (RCP-001/002 with original wording), media references, SEO fields, taxonomy, and an external source
reference.
CONSTRAINT: API-002; api-contract.md and content.md.
RESTRICTION: Versioned; additive changes only within v1. No HTML fields; formatted text uses blocks.
BEHAVIOR: Show the schema with complete examples, wait for approval, implement with schema tests.
```

### PP18.5 Publish Format mapper

```text
SCOPE: Map a validated Publish Format document to post and recipe drafts through the existing facades.
CONSTRAINT: API-002, API-007; Template A; recipes.md and content.md.
RESTRICTION: Same validation and sanitization as the editor. Preserve the sender's ingredient and step wording.
No direct repository access.
BEHAVIOR: Plan, wait for approval, implement with golden tests and a malicious-input corpus.
```

### PP18.6 Upsert draft endpoint

```text
SCOPE: Add PUT /api/publishing/v1/posts/{externalId} that creates or updates a draft keyed by the sender's
external source reference, with idempotency keys.
CONSTRAINT: API-003, API-005; Template B.
RESTRICTION: Never overwrites a draft edited in PantryPress since the last API write without reporting a
conflict (DECIDE: conflict or create a new revision).
BEHAVIOR: Plan, wait for approval, implement with replay, conflict, and two-site tests.
```

### PP18.7 Publish, schedule, unpublish, and status endpoints

```text
SCOPE: Add endpoints to publish now, schedule, unpublish, and read status and live URL for an API-managed post.
CONSTRAINT: API-003, API-005; Template B.
RESTRICTION: Requires the posts:publish scope. Uses the same publish readiness check and domain operations as
the editor.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP18.8 Media ingest

```text
SCOPE: Add media upload through the API and fetch-from-URL ingest, both feeding the PP6 scan and derivative
pipeline.
CONSTRAINT: API-004, PSEC-006; Template F and G.
RESTRICTION: URL fetch: https only, no private or link-local addresses after DNS resolution, bounded
redirects, size and type limits, and timeouts.
BEHAVIOR: Plan, wait for approval, implement with SSRF and oversized-file tests.
```

### PP18.9 Entitlements and audit for API calls

```text
SCOPE: Apply entitlement checks (storage, sites, plan status) and audit records to every publishing API write.
CONSTRAINT: API-006, ARCH-010.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP18.10 OpenAPI, contract tests, and sample client

```text
SCOPE: Publish OpenAPI for /api/publishing/v1, a contract test suite runnable against any environment, and a
minimal sample client that publishes a recipe post.
CONSTRAINT: API-009; api-contract.md.
BEHAVIOR: Plan, wait for approval, implement, and run the suite under aspire run.
```

### PP18.11 "Connect CreatorPantry" screen

```text
SCOPE: Add a guided screen that explains the connection, creates a scoped key, shows it once with a copy
button, and lists connected keys with last-used times and revoke.
CONSTRAINT: API-008, BIZ-010; Template H.
RESTRICTION: Plain language; no API jargon on the main path.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP18.12 Publishing API audit

```text
SCOPE: Audit PP18 against API-001–009, ARCH-009, BIZ-010, and PSEC-008 using the contract suite. Report only.
CONSTRAINT: Template L.
UTILIZATION: api-contract-checker, site-isolation-auditor, integration-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

## PP19 — Operations and launch readiness

### PP19.1 Production topology confirmation

```text
SCOPE: Confirm the requirements section 5.1 topology, SKUs, regions, and monthly cost estimate. Document only.
CONSTRAINT: OPS-001, OPS-009, PNFR-004.
BEHAVIOR: Show the table and cost, wait for approval, record it in the decisions document.
```

### PP19.2 Production IaC

```text
SCOPE: Generate production IaC: the shared platform module (PP9.1) for production, and PantryPress's own
resources: Container Apps apps (including the pantrypress-site SSR app, reachable only through Front Door),
its own SQL server, Redis, storage account, Key Vault, DNS zone, Communication Services, managed identities,
and private endpoints where the environment supports them.
CONSTRAINT: OPS-001, PP-D16; aspire-deployment.
RESTRICTION: No production apply in this prompt.
BEHAVIOR: Plan, wait for approval, generate, validate with what-if, and report.
```

### PP19.3 CI additions

```text
SCOPE: Add theme golden files, accessibility, performance budget, Rich Results validation, and public-site
reviewer checks to the CI pipeline.
CONSTRAINT: OPS-002.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP19.4 Deployment workflow

```text
SCOPE: Add the deployment workflow for every PantryPress app, including PantryPress.Site and pantrypress-site, with health-gated rollout and rollback, proving
rendered sites are unaffected by an app deploy.
CONSTRAINT: OPS-002, OPS-003.
BEHAVIOR: Plan, wait for approval, implement and dry-run.
```

### PP19.5 Platform admin: site search and detail

```text
SCOPE: Add PlatformAdmin site search by slug, domain, or account, with plan, usage, status, and audit trail.
CONSTRAINT: OPS-006; Template B and H.
RESTRICTION: PlatformAdmin only; every view of private data is audited.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP19.6 Platform admin actions

```text
SCOPE: Add suspend, restore, force page-data rebuild, and purge cache actions with reasons and audit.
CONSTRAINT: OPS-006, SITE-008.
BEHAVIOR: Plan, wait for approval, implement with authorization tests.
```

### PP19.7 Abuse reporting and takedown

```text
SCOPE: Add a public "report this site" form on every site and the takedown workflow: triage, suspend, notify,
and appeal.
CONSTRAINT: OPS-007, BIZ-009.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP19.8 Safe Browsing check on publish

```text
SCOPE: Check outbound links in a published revision against Google Safe Browsing before publish and hold flagged posts.
CONSTRAINT: OPS-007; Template F.
RESTRICTION: Fail open with a held-for-review state only if the provider is down (DECIDE), never silently.
BEHAVIOR: Verify the current API, plan, wait for approval, implement with fake tests.
```

### PP19.9 New-site velocity limits

```text
SCOPE: Limit new sites per account and per IP over time, and flag bursty sign-ups for review.
CONSTRAINT: OPS-007, BIZ-009.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP19.10 Monitoring and alerts

```text
SCOPE: Add alerts for edge 5xx, origin latency, render backlog age, webhook failures, email bounces,
certificate failures, and spend against budget, routed to the operator.
CONSTRAINT: OPS-004, OPS-009.
BEHAVIOR: Plan thresholds, wait for approval, implement, and fire each alert once in staging.
```

### PP19.11 Status page

```text
SCOPE: Publish a status page for edge, publishing, and admin, fed by synthetic checks.
CONSTRAINT: OPS-005.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP19.12 Reader data requests

```text
SCOPE: Add erasure of a reader's comments, ratings, and subscriptions across a creator's sites, and extend
creator export to include site content and subscriber CSV.
CONSTRAINT: OPS-011, ACCT-004, BIZ-006, CMP-002.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP19.13 Platform legal pages

```text
SCOPE: Publish platform Terms, Acceptable Use, Privacy Policy, DPA, sub-processor list, and DMCA agent page.
CONSTRAINT: CMP-001, CMP-002.
RESTRICTION: Drafts are flagged for legal review before launch.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP19.14 Runbooks

```text
SCOPE: Write the OPS-012 runbooks and link them from alerts.
CONSTRAINT: OPS-012.
BEHAVIOR: Plan, wait for approval, write.
```

### PP19.15 Restore and load drills

```text
SCOPE: Run a SQL point-in-time restore drill, a full-site page-data rebuild from published revisions, a full-site purge with SSR under load, and a viral-spike load test
against staging.
CONSTRAINT: OPS-008, PNFR-004–006.
BEHAVIOR: Plan, wait for approval, run, and report timings.
```

### PP19.16 Launch gate

```text
SCOPE: Audit the whole PantryPress release against every requirement ID. Report only.
CONSTRAINT: Template L.
UTILIZATION: All reviewers.
BEHAVIOR: Return blockers first, then findings, traceability, and "not verified." Wait for approval before fixes.
```


## PP20 — WordPress import

### PP20.1 Import job model

```text
SCOPE: Add ImportJob and ImportItem with resumable state and a per-item report.
CONSTRAINT: IMP-004; Template C.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

### PP20.2 WXR upload and parse

```text
SCOPE: Accept a WXR upload and parse it into a staging model of posts, pages, categories, tags, media URLs, and
comments.
CONSTRAINT: IMP-001, IMP-004; add-media-feature for the upload.
RESTRICTION: XML parsing with DTD and external entities disabled. All content is untrusted.
BEHAVIOR: Plan, wait for approval, implement with malicious-file tests.
```

### PP20.3 Recipe plugin extraction

```text
SCOPE: Extract WP Recipe Maker and Tasty Recipes data into structured PantryPress recipe drafts.
CONSTRAINT: IMP-002; Template D; recipes.md.
RESTRICTION: Preserve the blogger's wording verbatim. Imported recipes are drafts.
BEHAVIOR: Plan, wait for approval, implement with fixture tests.
```

### PP20.4 Post and page conversion

```text
SCOPE: Convert WordPress post and page HTML into the block document, mapping recipe shortcodes and blocks to
recipe card blocks.
CONSTRAINT: IMP-001, IMP-004, POST-002; content.md.
RESTRICTION: Unknown markup becomes paragraphs or is dropped and reported, never stored as raw HTML.
BEHAVIOR: Plan, wait for approval, implement with fixture tests.
```

### PP20.5 Media import

```text
SCOPE: Fetch referenced media through the PP18.8 ingest path into the media library and rewrite references.
CONSTRAINT: IMP-001, PSEC-006; Template G.
BEHAVIOR: Plan, wait for approval, implement with SSRF and failure tests.
```

### PP20.6 Comment import

```text
SCOPE: Import approved comments into the Audience module, linked to the imported posts.
CONSTRAINT: IMP-001, ENG-002.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP20.7 Redirects and review-then-publish

```text
SCOPE: Generate 301s from old permalinks and add a review screen where the blogger publishes imported posts in
bulk.
CONSTRAINT: IMP-003, IMP-004; Template H.
RESTRICTION: Nothing publishes without confirmation.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP20.8 Import audit

```text
SCOPE: Audit PP20 against IMP-001–004 and BIZ-011 with real sample exports. Report only.
CONSTRAINT: Template L.
UTILIZATION: site-isolation-auditor, public-site-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

---

## Template A — One backend seam

```text
SCOPE: Implement only [entity | repository method | DataLayer operation | Business rule | Facade operation |
controller endpoint] for [requirement ID and outcome].
CONSTRAINT: CLAUDE.md, .claude/rules/backend.md, and [nearest rule]; use [matching skill].
RESTRICTION: Touch only [allowed projects/files]. Do NOT pull forward the next layer, change unrelated
contracts, add dependencies, or weaken site isolation or content invariants.
UTILIZATION: [matching skill]; architecture-reviewer after implementation.
BEHAVIOR: Inspect the adjacent pattern, show the change and test plan, wait for approval, implement, run
focused tests, run the reviewer, and summarize files plus evidence.
```

## Template B — One authenticated endpoint

```text
SCOPE: Add [HTTP method/path] for [one elementary process] through Controller → Facade → Business →
DataLayer → Repository | Gateway.
CONSTRAINT: api-contract.md and backend.md; add-endpoint skill; [requirement ID].
RESTRICTION: Controller injects only the Facade. No EF or provider types at the boundary. No client-supplied
SiteId. Unknown and inaccessible resources are indistinguishable.
UTILIZATION: add-endpoint; api-contract-checker; site-isolation-auditor.
BEHAVIOR: Show contracts and per-layer responsibilities, wait for approval, implement with layer tests and a
two-site endpoint test, then review and report.
```

## Template C — Site-owned entity and migration

```text
SCOPE: Add [entity] as site-owned data for [requirement], with EF configuration and a generated migration.
CONSTRAINT: tenancy.md and backend.md; add-site-entity skill.
RESTRICTION: Required SiteId, global filter, server-side stamp, SiteId-leading indexes, and immutable
ownership. Create the migration but do NOT apply it until reviewed. No IgnoreQueryFilters.
UTILIZATION: add-site-entity; site-isolation-auditor.
BEHAVIOR: Defend the data-zone choice, show entity, indexes, delete behavior, and migration, wait for approval,
apply through MigrationService, test both sites, and confirm no pending model changes.
```

## Template D — Recipe or content mutation

```text
SCOPE: Add [one recipe or post mutation] for [requirement ID], producing [draft | immutable revision | state
transition] with explicit concurrency behavior.
CONSTRAINT: recipes.md or content.md, and tenancy.md; add-recipe-feature skill.
RESTRICTION: Preserve the blogger's wording. Never update a published revision. No silent last-write-wins.
Calculations stay deterministic.
UTILIZATION: add-recipe-feature; site-isolation-auditor and test-gap-analyzer.
BEHAVIOR: Show invariants, transaction, revision, and conflict behavior; wait for approval; implement with
success, no-op, conflict, rollback, and two-site tests; report.
```

## Template E — Editor block

```text
SCOPE: Add the [block] to the block schema, the editor, the server validator, and the HTML renderer.
CONSTRAINT: content.md and frontend.md; add-editor-block skill; POST-002.
RESTRICTION: Schema change is versioned. Server validation rejects anything the editor cannot produce. No raw
HTML storage.
UTILIZATION: add-editor-block; design-review; public-site-reviewer.
BEHAVIOR: Show schema, editor behavior, and rendered output, wait for approval, implement with round-trip and
golden tests.
```

## Template F — External integration

```text
SCOPE: Add [one provider operation] through a typed Gateway and stable internal contract for [requirement].
CONSTRAINT: external.md and [nearest rule]; add-external-integration skill.
RESTRICTION: Provider DTOs and IDs stay at the boundary. No credential in source, logs, or output. Timeouts,
bounded retry, rate-limit handling, cancellation, idempotency, and auditable outcomes. No live call in
default CI.
UTILIZATION: add-external-integration; integration-reviewer.
BEHAVIOR: Verify the current official contract, show mapping and resilience plan, wait for approval,
implement with recorded or fake contract tests, review, and report.
```

## Template G — Background job

```text
SCOPE: Add [one durable job] for [requirement] with persisted state, claim, retry, cancellation, idempotency,
and recovery.
CONSTRAINT: backend.md, tenancy.md, and external.md; add-background-job skill.
RESTRICTION: Re-resolve the site before calling a facade. No provider call inside a SQL transaction. Duplicate
delivery has no duplicate effect.
UTILIZATION: add-background-job; architecture-reviewer and site-isolation-auditor.
BEHAVIOR: Show the state machine and failure matrix, wait for approval, implement with competing-worker,
restart, replay, poison, cancellation, and two-site tests, then review.
```

## Template H — Admin UI slice

```text
SCOPE: Implement only [one screen, state, or component] for [requirement] in pantrypress-admin using
@pantrypress/ui.
CONSTRAINT: frontend.md and design-system.md; add-admin-component skill.
RESTRICTION: No HttpClient in components, literal colors, duplicated backend rules, drag-only actions, or
unrelated screens.
UTILIZATION: add-admin-component; design-review and api-contract-checker.
BEHAVIOR: Show the component, states, and accessibility plan, wait for approval, implement loading, empty,
error, degraded, and success states with tests, review, and report.
```

## Template I — Defect repair

```text
SCOPE: Reproduce and fix [one defect], then add the smallest regression test that proves the root cause.
CONSTRAINT: The nearest rules and the existing seam.
RESTRICTION: Minimal root-cause change only. No adjacent refactors or symptom suppression.
UTILIZATION: test-gap-analyzer after.
BEHAVIOR: Reproduce first, present evidence and the root cause, wait for approval, fix, add the test, run
focused and broader suites, and report before and after.
```

## Template J — Refactor with stable behavior

```text
SCOPE: Refactor [target] to [goal] without changing observable behavior. List every intended file first.
CONSTRAINT: Repository rules and existing contracts.
RESTRICTION: No schema, route, response, rule, authorization, tenancy, or visual change.
UTILIZATION: architecture-reviewer and api-contract-checker.
BEHAVIOR: Return the impact map and characterization tests, wait for approval, refactor in small green steps,
stop if the blast radius grows, then review and summarize.
```

## Template K — Migration review

```text
SCOPE: Review the generated migration [name] for [model change]. Do not apply it yet.
CONSTRAINT: backend.md and tenancy.md.
RESTRICTION: Report destructive changes, locks, backfills, SiteId and index mistakes, cascades, rollback limits,
and deployment ordering.
UTILIZATION: architecture-reviewer.
BEHAVIOR: Show the model diff, operations, and deployment plan, wait for approval, then apply through
MigrationService and confirm no pending model changes.
```

## Template L — Audit or release gate

```text
SCOPE: Audit [phase or release] against [requirement IDs]. Report only; make no edits.
CONSTRAINT: Read the named requirements and rules before evaluating.
RESTRICTION: Every pass or finding needs file, line, test, trace, rendered output, or runtime evidence. Put
unavailable checks under "Not verified."
UTILIZATION: The relevant read-only reviewers; site-isolation-auditor whenever site data is touched.
BEHAVIOR: Return blockers first, then high, medium, and low, plus traceability and not-verified sections. Wait
for approval before any fix.
```

## Template P1 — One public endpoint

```text
SCOPE: Add [method/path] on PantryPress.Site for [one reader action] through Controller → Facade → Business →
DataLayer → Repository | Gateway.
CONSTRAINT: public-endpoints.md and sites.md; add-public-endpoint skill; [requirement ID].
RESTRICTION: SiteId from Host only. No site identifier in the payload. Rate-limited. Untrusted input. Works
without JavaScript where it backs a form. No SQL reachable from a page request.
UTILIZATION: add-public-endpoint; public-site-reviewer; site-isolation-auditor.
BEHAVIOR: Show contracts, limits, and abuse cases, wait for approval, implement with two-site, spam, and
rate-limit tests, then review and report.
```

## Template P2 — One theme component

```text
SCOPE: Implement [Angular standalone component] for [theme(s)] in @pantrypress/ui/themes (or
projects/pantrypress-site for page composition) using theme tokens and the existing component contract.
CONSTRAINT: rendering.md and design-system.md; add-theme-component skill; PP-D07, PP-D08; THM-002, THM-003,
RND-007.
RESTRICTION: SSR-safe: page data only, no HttpClient, no browser APIs during render. Hydrates only inside a
@defer (hydrate on …) island. Semantic HTML, no literal colors, no new third-party origin, within budget.
UTILIZATION: add-theme-component; public-site-reviewer.
BEHAVIOR: Show markup and states, wait for approval, implement with golden, axe, and budget tests.
```

## Template P3 — One derived render artifact

```text
SCOPE: Generate [route page data | feed | sitemap | index] for a site after publish.
CONSTRAINT: rendering.md; add-render-artifact skill; ARCH-014.
RESTRICTION: Built from published revisions only. Deterministic, incremental where possible, written under the
page-data prefix, switched atomically.
UTILIZATION: add-render-artifact; public-site-reviewer.
BEHAVIOR: Show inputs, outputs, and rebuild triggers, wait for approval, implement with golden tests.
```

## Requirement-to-phase map

| Requirements | Primary phase |
| --- | --- |
| ARCH-001, section 6 | PP0 |
| ACCT-001–005; PSEC-003 | PP1 |
| SITE-001–005, 008–010; ARCH-003, 015; PSEC-004, 007 | PP2 |
| PP-D07 (design system) | PP3 |
| RCP-001–009 | PP4 |
| POST-001–008, 010; BIZ-001 | PP5 |
| MED-001–005; PSEC-006 | PP6 |
| RND-001–008; ARCH-004–008, 012–014; THM-007; THM-001 (Market preset) | PP7 |
| SEO-001–007; SITE-006–007, 011–012; POST-011; PP-D18 | PP8 |
| PSEC-001–002; PNFR-001–002; POST-009; PP-D13, PP-D16 (non-production) | PP9 |
| BIZ-002; THM-004; SITE-004, 006 | PP10 |
| PP-D17; THM-001–013; RCP-010; RND-007; CMP-006; PNFR-003, 005 | PP11 |
| ENG-001–004, 008; PSEC-005 | PP12 |
| ENG-005–007; POST-011 (contact block); CMP-003 | PP13 |
| BILL-001–008; ARCH-010, 011; BIZ-004 | PP14 |
| MON-001–005; CMP-004 | PP15 |
| ANL-001–004 | PP16 |
| DOM-001–005 | PP17 |
| API-001–009; ARCH-009; BIZ-010; PSEC-008 | PP18 |
| OPS-001–012; CMP-001, 002, 005; PNFR-004, 006, 007; BIZ-006, 008, 009; ACCT-004 | PP19 |
| IMP-001–004; BIZ-011 | PP20 |
| ARCH-002 | Every prompt |
| BIZ-003, BIZ-005, BIZ-007 | Spread across PP4–PP17; verified at PP19.16 |

## Definition of done for one microprompt

A prompt is complete only when:

- its one declared observable outcome exists and no excluded adjacent capability was pulled forward;
- focused tests and affected broader tests pass;
- a two-site test proves isolation whenever site-owned, public, or reader data is touched;
- no SQL, model call, or cross-site access is reachable from an anonymous page request;
- the blogger's wording is preserved and calculations are deterministic;
- rendered output is deterministic and golden files changed only where intended;
- performance budgets and accessibility checks pass for every affected preset and option value;
- the per-site CSP admits only the origins the site's integrations need;
- reader and API input is untrusted, validated, sanitized, and rate-limited;
- entitlement checks go through the entitlements facade;
- user-facing copy avoids infrastructure jargon and keeps advanced options behind progressive disclosure;
- named reviewer findings are resolved or recorded;
- docs and traceability are updated when a contract or decision changed;
- the final report names files changed, commands run, and remaining limitations.

## Final reminders

- PantryPress stands alone. It never calls CreatorPantry; CreatorPantry publishes in through the API.
- Shared Azure means a shared Front Door profile, Container Apps environment, and monitoring workspace; data
  stores are PantryPress's own.
- Page data comes from published revisions, never drafts.
- Cache first: public pages must keep working with SQL, ApiService, and the Worker down.
- Host is the only tenant signal on the public side; route plus membership on the admin side; the key on the
  publishing API.
- Studio customization (PP-D17) means typed settings, never custom code.
- Pages are built from blocks and shared sections (PP-D18). If a prompt adds columns, free positioning, or
  per-element styling, stop.
- Newsletter means signup and CSV export only. If a prompt starts sending email to subscribers, stop.
- No AI anywhere in PantryPress.
- Custom domains are opt-in; the subdomain always works.
- One developer: if a prompt adds a schema, endpoint, UI, provider, and job at once, split it.

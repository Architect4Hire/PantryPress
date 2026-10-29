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

**These are deliberately small.** The document has **21 phases (PP0–PP20) and 258 microprompts**. Run one
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
- PantryPress's own landing page is a companion document, `docs/prompts/pantrypress-landing-page-scrub-prompts.md`
  (PPL.1 to PPL.22). Its PPL.1 to PPL.3 move the gateway to the platform home on `pantrypress.app` (PP-D19, ADR 0019)
  and run before PP3.8; its "When each prompt runs" table places the rest among these phases.

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
  `trustProxyHeaders` behind the `X-Azure-FDID` check, with two-site host-header tests - superseded at PP2.14:
  `trustProxyHeaders` stays off, and the reader's host is the edge-proven `X-PP-Reader-Host`). `NG_ALLOWED_HOSTS` was
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

### PP0.8 First complete local run - done

```text
SCOPE: Run the empty system and repair only wiring that blocks startup.
CONSTRAINT: aspire.md.
RESTRICTION: No feature work.
UTILIZATION: architecture-reviewer.
BEHAVIOR: Give one health line per resource, list startup fixes with evidence, leave the build clean.
```

Delivery note: no startup fixes were needed and no code changed. `aspire start` (Aspire CLI 13.6.3) brought up
every resource on the first run: `sql`, `pantrypress`, `redis`, `storage`, `blobs`, `sql-password`, and
`redis-password` were healthy; `migrationservice` finished with exit code 0 ("0 migrations applied", since there
are no migrations yet); `apiservice`, `site`, `worker`, `gateway`, and `admin` were healthy, and `admin-installer`
finished. The `/health` and `/alive` probes returned 200 on ApiService, Site, Worker, and the gateway (HTTP and
HTTPS at `app.localhost`), and the gateway served the admin SPA. No resource logged a warning or error. This
differs from the checkpoint: only one Angular app runs, because the `pantrypress-site` Node SSR resource arrives in
PP7.2a. Recorded for later prompts:

- **PP1.5:** the gateway has no `apiservice` reference yet, so it cannot report API reachability (gateway.md);
  add `WaitFor(apiservice)` or a readiness check along with the reference.
- **PP1.6/PP19:** the `admin-spa` route follows the gateway's environment, but the `admin` resource follows the
  AppHost's. Set the gateway's environment from the AppHost, or gate the route on the `admin` service-discovery
  entries.
- **PP7.2a:** replace the hand-kept dependent list in `MigrationServiceTests` with a rule (every project or
  executable except `migrationservice` waits for it to complete) before the SSR resource is added.
- **PP19.2:** `WithHttpHealthCheck("/health")` probes will 404 in production while health endpoints stay
  Development-only. Key Vault (PP-D16) is not modeled until the first provider secret arrives.
- Redis and blobs have no consumers yet. Each consumer adds `WithReference` plus `WaitFor`.

Verification: `aspire start` with every resource in the states listed above, `dotnet build` (0 warnings),
`dotnet test` (73 passed), `dotnet format --verify-no-changes`, `npm run lint`, `npm test` (3 suites),
`npm run format:check`, and `npm run build`. Reviewer: architecture-reviewer, with no blocking or PP0.8 findings;
the rest is recorded above.

## PP1 — Identity, gateway, and browser boundary

### PP1.1 ApplicationUser and identity store - done

```text
SCOPE: Add ApplicationUser to PantryPressDbContext with ASP.NET Core Identity stores and the initial
identity migration.
CONSTRAINT: ACCT-001; auth.md.
RESTRICTION: No endpoints yet. Migration reviewed before it is applied.
BEHAVIOR: Plan, wait for approval, implement, apply through MigrationService, test.
```

*PP1.1 delivery note (recorded at PP1.12):* shipped before this file kept delivery notes, and found unmarked by the PP1.12
audit. `ApplicationUser` (`Modules/Accounts`) over Identity's EF Core stores in `PantryPressDbContext`, with the Identity
schema pinned to Version 2 with bounded keys and no passkeys, and the unique `EmailIndex` that is the last word on
concurrent sign-ups; migration `InitialIdentity`, applied only by MigrationService. Evidence: `IdentityModelTests`
(the model holds the Identity stores and nothing undeclared; the pinned schema; the unique email index; a host that did
not pin the schema is refused), `IdentityStoreTests`, and `MigrationServiceRegistrationTests` (the design-time and
registered models match the migrations).

### PP1.2 Registration and email verification - done

```text
SCOPE: Add registration and email verification through the request seam, with a development email sink.
CONSTRAINT: ACCT-001, ACCT-003; Template B.
RESTRICTION: Publishing is blocked until email is verified. Rate-limited. No enumeration of existing emails.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*Split (owner-approved 2026-10-23):* the prompt needs a provider, host wiring, and endpoints, so it runs as one seam
each: **PP1.2a** email gateway and development sink, **PP1.2b** ApiService foundation (result types, problem
details, validation, OpenAPI, database and Identity registration), and **PP1.2c** registration and verification
endpoints. PP1.2 is marked done after PP1.2c. Approved defaults for PP1.2c: account routes under
`/api/v1/account/...`; verification email sent after commit until the PP1.10 outbox, with resend as recovery; no
enumeration (same `202` for new and existing addresses, password hashed on every path, the `EmailIndex` race
taken as "existing"); a global route limit plus a per-address email throttle in the facade, with per-IP limits at
the gateway (PP1.5, PP1.8); disposable domains from an embedded CC0 list; password length 12–128 with no
composition rules; sign-in allowed before verification; an Accounts publishing-eligibility contract for PP5; a
typed `AccountId`; Identity's default token provider (24 hours) on the development key ring. The spam challenge
moves to PP12.3 and must be on sign-up before public sign-up opens (BIZ-009).

*PP1.2a delivery note (done):* `IEmailGateway`, `EmailMessage`, and `EmailDeliveryException` live in
`Domain/Managers/Email`. Email is shared by Accounts, Audience, contact, and Billing, so no single module owns
it, and the shared kernel is the gateway's home. The interface is in domain terms:
- a caller-chosen message id, stable across retries and sent as `Message-ID`;
- one bare recipient;
- a single-line subject with no control or Unicode line-break characters;
- `ToString` shows only the id.

Gateways throw only `EmailDeliveryException`, with a transient or permanent flag and no inner provider exception,
because SMTP errors quote the recipient. The local fake is `Integrations/DevelopmentMail`, an SMTP sink with these
safeguards:
- it accepts only loopback hosts, checked at startup;
- it bounds each send with a token, because `SmtpClient.Timeout` does not apply to async sends.

It delivers to a `mail` container (`axllent/mailpit:v1.31.2`, version check off, no volume) declared only in the
local Development run-mode block. Tests use `RecordingEmailGateway`. Nothing consumes the gateway yet; ApiService
wiring arrives with PP1.2c. Recorded for later prompts:

- **PP1.2c:** register the sink only in Development. Outside Development, with no real gateway yet, the host fails
  to start rather than falling back. Reference the `mail` endpoints from `apiservice` inside the run-mode
  Development block.
- **PP13.1:** the sender address is an Azure Communication Services setting (parameter or Key Vault), never part of
  the message. A contact-form `ReplyTo` needs the same bare-address check. Consider a closed email-kind list
  (PP-D14). Set the Azure SDK retry and timeout explicitly and keep its content logging off. Map the message id to
  the provider's operation id. Add a boundary test that keeps provider types inside `Domain/Integrations/`.
- No test yet proves a non-Development run has no `mail` or `admin` resource; publish mode is covered.

Verification: `dotnet build` (0 warnings), `dotnet test` (121 passed, including the Mailpit round trip with UTF-8
text, HTML, and `Message-ID`), `dotnet format --verify-no-changes`. Reviewer: integration-reviewer; the five
should-fix items were fixed and the rest is recorded above.

*PP1.2b delivery note (done):* the shared kernel gains `Managers/Results` (`Result` and `Result<T>`, sealed classes so
an unset result is never read as success, and `Error` with a kind, a stable code, a plain title, and field messages)
and `Managers/Validation` (FluentValidation at the facade edge, errors keyed by the JSON field name). ApiService now
has:
- the database through the Aspire client (`WithReference(db)` in the AppHost), with the Identity schema pinned;
- controllers with camel-case JSON and strict kebab-case enums;
- RFC 9457 problem details with a `code` on every failure, including framework failures (404, 405, 415, 429, 500)
  and binding errors, which get plain messages instead of parser text, and 500s that never carry exception details;
- `Cache-Control: no-store` on every response by default;
- the OpenAPI document in Development only, with `code` declared on the problem schemas and 429 and 500 on every route;
- data protection with the application name `PantryPress`.

*PP1.2c delivery note (done):* `POST /api/v1/account/register`, `/email/verify`, and `/email/resend` through
`AccountController` → `IAccountRegistrationFacade` → `AccountsDataLayer` → `AccountUserStore`. Approved defaults apply.
These differ from the approved text or add to it:
- **Link tokens:** a PantryPress token provider replaces Identity's default. Same 24 hours, same protection, bound to
  the account, purpose, and security stamp, but timed by `IClock`, because Identity's provider reads the system clock.
- **Links:** the admin app origin comes from `Accounts:AppBaseUrl`, set by the AppHost (the gateway at
  `https://app.localhost:{port}` locally; the `app-base-url` parameter elsewhere), never from a Host header. Tokens
  sit in the URL fragment (`/verify-email#account=…&token=…`), so they never reach server or gateway logs.
- **Unit of work:** `AccountUserStore` turns off Identity's auto-save, and `AccountsUnitOfWork` commits once per facade
  call. Identity's own `UpdateAsync` re-attaches the user and breaks the concurrency check on a second update, so the
  store overrides it.
- **Email:** sent after commit through an in-process `IEmailQueue` (`Managers/Email`) drained by a background worker
  that retries transient failures twice. A crash loses queued mail; resend is the recovery until PP1.10 (replaced by the
  outbox at PP1.10b). The
  per-address throttle (1 a minute, 5 an hour, per kind) is stored in Identity's user-token table, so no migration.
- **Disposable domains:** the CC0 blocklist from `disposable-email-domains` (commit b5e699f, 9,925 domains),
  embedded; parent domains are matched.
- **Publishing check:** `IAccountPublishingEligibility` returns `email-not-verified` for PP5.

Recorded for later prompts:
- **PP1.5:** route `/api/v1/account/*` through the gateway, and add per-IP limits: the anonymous account routes share
  one global bucket (120 a minute), which one caller could exhaust.
- **PP1.9:** a client's `Idempotency-Key` is ignored today.
- **PP1.10:** replace `InProcessEmailQueue`, then forbid `Microsoft.Extensions.Hosting` in Domain's boundary test.
- **PP2:** add a module boundary test: other modules use only `Modules.Accounts.Contracts`.
- **PP5:** publish and schedule call `IAccountPublishingEligibility`.
- **PP12.3:** sign-up for a new address does more database work than for a registered one; review timing before
  public sign-up opens.
- **PP19:** set `app-base-url`, and keep the data protection key ring persistent (see PP1.3b).
- Validation errors about the whole body use the key `""` in `errors`; the admin client must handle it.

Verification for PP1.2b and PP1.2c, together with PP1.3 below: `dotnet build` (0 warnings), `dotnet test` (243
passed), and `dotnet format --verify-no-changes`. The tests include:
- account API tests through the real stack on SQL in a container, with recorded email and a fake clock;
- concurrent sign-ups for one address;
- an end-to-end run under the AppHost, where a sign-up on the running ApiService lands in Mailpit with a gateway
  link;
- AppHost wiring tests for run and publish modes;
- OpenAPI checks on statuses, problem content types, and `code`.

Reviewers: architecture-reviewer and api-contract-checker. Neither reported a blocking finding. Should-fix items
fixed:
- the publish-mode link origin, now a parameter;
- EF Core moved out of the data layer;
- the throttle decision moved to the facades;
- `Result` as classes;
- undeclared 404, 409, and validation statuses;
- `code` added to the OpenAPI schema;
- plain titles for 400, 405, 413, and 415.

The rest is recorded above and under PP1.3.

### PP1.3 Password management and two-factor - done

```text
SCOPE: Add password reset and change, and optional TOTP two-factor with recovery codes.
CONSTRAINT: ACCT-001; auth.md.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*Split (owner-approved 2026-10-23):* **PP1.3a** password reset and change, and **PP1.3b** authenticator two-factor
with recovery codes. Approved defaults: reset links last 1 hour and work once; a reset also confirms the email;
recovery codes are stored hashed; the authenticator key is encrypted at rest; a code is accepted once; lockout after
5 failures for 15 minutes. Sign-in itself, and its code step, belong to PP1.6.

*PP1.3a delivery note (done):*
- **Routes:** `POST /api/v1/account/password/forgot` always answers 202, throttled per address like resend.
  `/password/reset` takes `{accountId, token, newPassword}` from the link fragment, not the email address the plan
  named, which keeps the address out of links. `/password/change` is for a signed-in caller.
- **Reset effects:** a reset confirms the email and also clears lockout; the plan did not name the lockout part.
- **Notices:** both reset and change rotate the security stamp, which voids outstanding links, and email a "password
  was changed" notice.
- **Lockout:** a wrong current password counts toward lockout.
- **Signed-in caller:** controllers read the account from the name-identifier claim, not through the planned
  `ICurrentAccount`. Until PP1.6, a `PendingSession` scheme authenticates nobody, so these routes answer 401 in real
  runs; tests sign in with their own scheme.

*PP1.3b delivery note (done):*
- **Routes:** `GET /api/v1/account/two-factor`, and `POST` to `/setup`, `/enable`, `/disable`, and `/recovery-codes`.
- **Setup:** makes a fresh key each time and refuses while two-step sign-in is on. Turning it on needs a valid code
  and returns 10 recovery codes once. Turning it off, or replacing the codes, needs the password. Each change emails
  a notice, and a used recovery code emails how many are left.
- **Codes:** RFC 6238 with a one-step window (Identity allows two). The accepted step is stored, so a code works once,
  and concurrent uses let exactly one through (tested).
- **Storage:** authenticator keys are encrypted with data protection. Recovery codes are 10 characters (50 bits) with
  no look-alike characters, stored as HMAC-SHA256 with a per-account salt, never as Identity's plain text.
- **Sign-in step for PP1.6:** `ITwoFactorFacade.VerifySecondFactorAsync` takes an authenticator or recovery code; it
  has no route.

Recorded for later prompts:
- **PP1.4:** require two-step sign-in for `PlatformAdmin` (auth.md).
- **PP1.6:**
  - replace `PendingSession` and put the account id in the name-identifier claim;
  - validate the security stamp, so a password or two-step change ends other sessions;
  - decide 401 or 404 for a session whose account is gone;
  - route sign-in through the Accounts facades, not `SignInManager` directly, because the store does not save on its
    own;
  - Identity's lockout reads the system clock, so lockout expiry is not `IClock`-testable.
- **PP1.9:** `password/change`, `password/reset`, and `two-factor/enable` fail on a retried request after a lost
  response; make them opt-in targets. `setup`, `enable`, and `recovery-codes` return secrets: never store their
  replayed responses in readable form.
- **PP1.11:** audit password reset and change, two-step on and off, recovery codes replaced, and recovery codes used.
- **PP19:**
  - the data protection key ring must be persistent and protected by Key Vault before any deployed account turns on
    two-step sign-in, or stored authenticator keys become unreadable; recovery codes still work;
  - lockout's 429 has no `Retry-After`; consider adding one.

### PP1.4 PlatformAdmin role and policy - done

```text
SCOPE: Add the PlatformAdmin role, an authorization policy, and an audited operator-only assignment path.
CONSTRAINT: ACCT-005; auth.md.
RESTRICTION: No self-service path to PlatformAdmin.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*PP1.4 delivery note (done):*
- **The role is reference data.** `PlatformAdmin` is seeded by the `PlatformAdminRole` migration with a fixed id, as
  platform data with no `SiteId` (PP-D03, PP-D06). No runtime code creates a role, so there is no code path for
  "make me an admin" to use. Rolling the migration back deletes the row, and Identity's cascade takes the
  assignments with it; the operator's list restores them on the next run forward.
- **The assignment path (owner-approved):** one configuration value, `Platform:Admins` — email addresses separated by
  commas, semicolons, or new lines — reconciled by the migration job right after it applies migrations. Listed
  accounts are granted, holders the list leaves out are revoked, and an empty or missing list changes nothing at all,
  so a lost secret cannot lock the operators out. An address with no account yet is a masked warning, not a failure;
  the next run picks it up. Any other failure fails the run, which leaves dependents unstarted.
- **No self-service.** There is no route, and `IPlatformAdminFacade`, `IPlatformAdminReconciler`, and `IAccountRoles`
  are registered only by `AddPlatformAdministration`, which only the migration job calls. The admin API cannot
  resolve them and no type in it asks for one; both are tested.
- **Seam.** `PlatformAdminReconciler → IPlatformAdminFacade (Modules/Platform) → IAccountRoles (Modules/Accounts) →
  AccountsDataLayer → AccountUserStore` (the data layer became `AccountRolesDataLayer` in the PP1.5 fold-in below).
  Platform reaches accounts only through the Accounts contract, which grants and revokes the one platform role and
  nothing else: there is no "add any role" call.
- **Sessions.** A real grant or revocation rotates the security stamp, so a revoked operator's existing sessions stop
  working once PP1.6 validates it. A repeat changes nothing and leaves the stamp alone, so running the list again
  never logs anyone out.
- **Policy.** The named `PlatformAdmin` policy needs the role *and* a passed second step
  (`AccountClaims.TwoFactorVerified`, `pp:mfa`), which is the PP1.3 note about requiring two-step sign-in for
  operators. No route carries it yet, so it is asserted directly through `IAuthorizationService`.
- **Audit (owner-approved):** each real change writes one structured event (`PlatformAdminGranted`,
  `PlatformAdminRevoked`) with the actor, the account id, and the reason, never an email address. PP1.11 adds the
  durable log, as it does for PP1.3's security events.
- **Not in this prompt:** no endpoint, operator console, or admin UI; no site roles; no claim issuance (PP1.6); no
  durable audit log (PP1.11).
- **Also changed:** `AddAccounts` and the new `AddAccountRoles` share an internal Identity registration and no host
  calls both. `AddAccountRoles` registers a refusing stand-in for data protection, because that process has no key
  ring and granting a role must never touch an authenticator key. The migrated-database test fixture became a
  collection fixture shared by the Identity and platform-role integration tests: one more AppHost per class outran
  what a developer machine starts inside the timeout.

Recorded for later prompts:
- **PP1.6:** the session must issue the role claims (`ClaimTypes.Role`) and `pp:mfa` only after the second step, and
  validate the security stamp, or a revoked operator keeps working with the cookie they hold.
- **PP1.11:** audit PlatformAdmin granted and revoked durably; the facade already emits both events with actor,
  account, and reason.
- **PP18, PP19:** operator endpoints attach the `PlatformAdmin` policy; the deployed `Platform:Admins` secret must
  reach the migration job from Key Vault, and the operator console's own grant path (if any) goes through
  `IPlatformAdminFacade` so it stays audited.

*PP1.4 review fold-in (applied at PP1.5):* the architecture review's two major findings and five of its minor ones
are in. **Secret parameter:** `Platform:Admins` now reaches the migration job as the secret Aspire parameter
`platform-admins`, always passed, so the list is masked in the dashboard, published as a parameter rather than
plaintext, and PP19's Key Vault hand-off is a binding; an empty value is still the reconciler's "no opinion".
**Role capability contained:** the role calls moved off the shared `AccountsDataLayer` onto `AccountRolesDataLayer`,
which only `AddAccountRoles` registers, and the role APIs are now banned symbols at both levels -
`UserManager.AddToRoleAsync`, `AddToRolesAsync`, `RemoveFromRoleAsync`, `RemoveFromRolesAsync`,
`GetUsersInRoleAsync`, the same three on `IUserRoleStore`, and `RoleManager.CreateAsync` (the role row is seeded by a
migration) - so a future facade that tries to change a platform role, directly or through the store it could cast to,
fails the build. Two files suppress the rule with a reason: `AccountRolesDataLayer`, and the Identity-store
integration test, which maps Identity's own tables with a throwaway role of its own. `.AddRoles<IdentityRole<Guid>>()` deliberately stays in the shared Identity
registration, against the review's suggestion: it also selects the claims principal factory that issues role claims,
which PP1.6's session needs, and removing it would not have removed the capability anyway (the user store implements
`IUserRoleStore` either way) - the ban is what removes it. **Module cycle:** the Identity role name now lives in
`Accounts.Contracts.AccountRoles` and `PlatformRoles` takes it from there, so the dependency runs Platform to Accounts
only - and `DomainBoundaryTests` now enforces the module graph by scanning module sources for references to another
module, because both members are `const` and the compiler inlines them, leaving nothing for a reflection test to see.
A probe confirmed the test fails when an Accounts file reaches into Platform. **Also:** the migration worker's duplicate "list applied" line is gone (the reconciler owns the summary);
`MigrationService` awaits `RunAsync`; `UnusedDataProtection` moved out of `Repositories/`; and
`TestDatabase.CreateOperatorServicesAsync` no longer pre-registers an ephemeral key ring, so every operator
integration test now runs the real composition and would fail if a role change ever reached for a protector.
Deferred with reasons: the reconcile rule's placement in a facade and its throw-instead-of-`Result` mechanism (both
placement, no behaviour change - they belong with PP1.11, which reworks that path for the durable audit log), and
warning when a granted account has no second step (m6: it fails closed today, and the warning needs a new Accounts
contract call, so it goes with PP1.6's claims work).

### PP1.5 Gateway routes and API versioning - done

```text
SCOPE: Configure YARP in PantryPress.Gateway to route the admin SPA's API calls to ApiService, add the API
versioning convention, and OpenAPI with a reference UI in development.
CONSTRAINT: ACCT-002; gateway.md and api-contract.md.
BEHAVIOR: Plan, wait for approval, implement with routing tests.
```

*PP1.5 delivery note (done):*
- **Routes (owner-approved reading).** `appsettings.json` declares one route for every environment:
  `/api/v1/{**rest}` to the `api` cluster, destination `http://apiservice` through service discovery. Everything else
  under `/api` is answered by the gateway itself with an RFC 9457 problem 404 (`not-found`, `no-store`): the publishing
  API keeps its own edge route and never crosses the BFF, the public site API answers on site hosts, and a version
  nobody declared does not exist. That tightens gateway.md's "`/api/*` proxied to ApiService" and closes PP0.7's note
  that unmatched `/api` paths fell through to the SPA with a 200. Orders are explicit - forwarded route 1, the
  gateway's fallback 2, the Development SPA catch-all 1000 - so which endpoint answers never rests on route-precedence
  subtleties. The bare prefix `/api/v1` is forwarded, and ApiService answers for it.
- **One error shape on the whole `/api` surface.** Both reviewers found the hole the 404 left open: YARP answers a
  forward it could not complete with a bodiless 502 or 504, which is exactly what the admin app would get when the API
  is down. The gateway now registers problem details with `UseExceptionHandler` and `UseStatusCodePages`, so a failed forward
  and an unhandled gateway fault both come back as RFC 9457 with a stable `code`, a plain title, `no-store`, and no
  exception detail. Codes and titles mirror `ApiProblems`, and a test compares the gateway's 404 with the API's field
  for field, values included.
- **Versioning is a convention, not a package.** `ApiSurfaces` lists each surface (route prefix, OpenAPI document,
  title) and `ApiSurfaceConvention` stamps every controller with its surface's document name and refuses to start the
  host when a route belongs to no surface: unversioned (`api/account`), an undeclared version (`api/v2/...`), or an
  action whose absolute route escapes the prefix. Controllers now read `ApiSurfaces.AdminV1Route + "/account"`.
  `Asp.Versioning` was considered and declined: its value is negotiating a version by header, query string, or media
  type, which api-contract.md does not want and which is the kind of client-supplied routing input this codebase
  refuses elsewhere; it also would not refuse a wrong route. Three guards came out of the reviews: MVC builds its
  action descriptors lazily, so the host now touches them before `Run`, which makes the refusal a failure to start
  rather than a failure on the first request; the convention sees controllers only, so a test walks every routable
  `/api` endpoint and asserts a surface covers it, which catches a minimal-API route too; and a test asserts no
  declared prefix covers another, since `ApiSurfaces.For` takes the first match. Each surface also carries its own
  `Version`, so PP18 can name a document `publishing-v1` without publishing that as the API's version.
- **OpenAPI and the reference UI.** One document per surface, each including only its own group, so PP18's publishing
  document cannot pick up admin routes and the admin document cannot pick up publishing routes. `/openapi/v1.json` is
  unchanged. `Scalar.AspNetCore` 2.18.3 serves the reference UI at `/scalar/v1` in Development only; the package
  embeds its script, which this app serves, and the default CDN fonts are off, so reading the documentation fetches
  nothing from the internet (a test asserts the page carries no `https://` URL and that both scripts answer 200). The
  Aspire dashboard links it on the `apiservice` resource in local Development only. Publishing the publishing-API
  document (API-009) stays with PP18.
- **API reachability (PP0.8's note).** The gateway references `apiservice` and reports reachability in its own
  `/health`: any HTTP answer counts as reachable - 401, 404, 503 - and only a refused connection, a DNS failure, or a
  timeout does not. Unreachable is **degraded**, not unhealthy, so a brief API outage does not take the gateway down
  with the admin app. The check is untagged, so `/alive` never calls the API, and the probe client has its resilience
  handlers removed, because a probe answers now or not at all. The gateway does not `WaitFor` the API: an API restart
  must not keep the admin app from loading.
- **Deferred with owner approval:** per-IP limits on the authentication routes, which PP1.2c recorded for this prompt,
  move to PP1.8. Per-IP partitioning before trusted-proxy configuration would put every request behind Front Door's
  address in one bucket - worse than today's global ceiling.
- **Not in this prompt:** the session cookie and sign-in (PP1.6); stripping forwarded and identity headers, CORS,
  antiforgery (PP1.7); security headers, body limits, trusted proxies (PP1.8); idempotency (PP1.9); the publishing
  surface, its document, and its edge route (PP18); serving the built admin files in production (PP19).

- **Integration tests share the running system.** The two end-to-end gateway tests use the `MigratedDatabase`
  collection fixture's running AppHost instead of starting their own. Starting one more was enough to tip the suite
  over: nine classes each bringing up SQL, Redis, Azurite, and five projects timed 35 tests out at five minutes. On the
  shared fixture the whole integration suite passes in 1m43s. The hand-kept list of classes that start their own
  AppHost is still the limiting factor, which is PP7.2a's recorded job.

Recorded for later prompts:
- **PP1.6/PP1.7:** the `admin-spa` route still passes `Cookie` through to the dev server, and the forwarded `/api/v1`
  route uses YARP's default `X-Forwarded` transforms. Both are PP1.7's to pin down with tests, together with dev HTTPS
  before the `__Host-` cookie tests.
- **PP18:** add the publishing surface to `ApiSurfaces` (`api/publishing/v1`) with its own document, and route it at
  the edge rather than through the gateway - the gateway's `/api` fallback already refuses it, and a contract test
  should keep it refused.
- **PP19/PP19.2:** production still has no SPA route; `/health` and `/alive` are Development-only, so the reachability
  report is a local signal until PP19.2 maps the probes for Container Apps.
- **Recorded from the reviews rather than fixed:** the logical name `apiservice` appears in the gateway's route
  configuration, the reachability probe, and the AppHost resource name, with no single constant tying them (the wiring
  test pins the reference, so a rename is caught); there is no test that a host carrying a bad controller route fails
  to start, only unit tests of the convention that would refuse it; `Scalar.AspNetCore` ships in production builds
  although nothing maps it outside Development, which the Production tests confirm is inert.
- **Pre-existing, unrelated:** `MigrationWorkerTests.Transient_errors_are_retried_then_fail_when_retries_run_out` and
  `Stopping_mid_run_exits_with_failure_and_logs_a_cancellation_not_an_error` fail at HEAD (ebd42b0) on this machine,
  verified in a clean worktree: the `ResettingSqlEndpoint` fake no longer provokes an EF retry (0 observed, 2
  expected). Nothing in this prompt touches that path.

Verification: `dotnet build PantryPress.slnx` (0 warnings), `dotnet test PantryPress.slnx` (336 of 338 passing; the
only red is the pre-existing `MigrationWorkerTests` pair above, which fails identically at HEAD), `dotnet format
PantryPress.slnx --verify-no-changes` (clean), and 46 new test cases: route matching and the problem 404 for every
non-forwarded `/api` path, a failed forward, the forwarded-prefix table against `ApiSurfaces`, the surface invariants
and the endpoint scan, the convention's refusals, the reference UI in Development and its absence in Production,
reachability healthy and degraded, liveness never probing, and the AppHost wiring. The end-to-end half runs on a real
Aspire AppHost: the admin API answering through the gateway at `app.localhost`, the publishing path refused there, the
gateway reporting `Healthy` against the live API, and `/openapi/v1.json` and `/scalar/v1` served by the running
ApiService. No separate `aspire run` was needed for this prompt - those tests bring up the same application model -
and a module-edge probe confirmed the new boundary test fails when an Accounts file reaches into Platform. Reviewers:
api-contract-checker and architecture-reviewer; both found the missing problem shape on a failed forward, which is
fixed above, and their remaining findings are either applied or recorded here.

### PP1.6 BFF session and cookies - done

```text
SCOPE: Implement BFF sign-in, sign-out, and session refresh with a __Host- prefixed, host-only, Secure,
HttpOnly, SameSite=Lax cookie.
CONSTRAINT: ACCT-002, PSEC-003, PP-D13; gateway.md and auth.md.
RESTRICTION: The browser never sees or stores a token.
BEHAVIOR: Plan, wait for approval, implement with cookie and cookie-tossing tests from a sibling subdomain.
```

*PP1.6 delivery note (done):*
- **The gateway holds the session; the API holds the truth (owner-approved plan).** `POST /api/v1/account/session`,
  `POST …/session/second-factor`, `GET …/session`, and `DELETE …/session` are answered by `PantryPress.Gateway`
  itself at YARP order 0 - ahead of the forwarded route (1) and the `/api` problem 404 (2) - because the gateway owns
  the cookie (gateway.md). It holds no rules: it asks ApiService, relays the API's problem document for a refusal a
  person is meant to read, and writes a cookie. `api-contract.md` and `gateway.md` now say so.
- **Two `__Host-` cookies.** `__Host-pp-session` for a session that may act (8-hour sliding, browser-session, not
  persistent) and `__Host-pp-pending` for a sign-in that owes its second step (5 minutes, fixed). Both are
  `Secure` in **every** environment, `HttpOnly`, `SameSite=Lax`, `Path=/`, with no `Domain` - the prefix plus the
  absent `Domain` is what stops a sibling tenant subdomain from writing one (PP-D13, PSEC-003). Two schemes rather
  than one claim, so a half-finished ticket is not even readable as a complete one. The second cookie was not in the
  prompt; it is what made "a right password reaches nothing" true.
- **The ticket is an account id and that account's security stamp - never a token.** The gateway forwards both as
  `X-PP-Account` and `X-PP-Stamp`, with `X-PP-Mfa` when the second step was passed, behind `X-PP-Gateway`: a secret
  the AppHost generates for those two processes alone and compares in constant time. Inbound `X-PP-*` and `Cookie`
  are stripped from every forwarded request. The headers carry no authority: ApiService reloads the account on every
  request, compares the stamp, and builds the claims - roles included - from the database, which is what makes a
  changed password, a changed second factor, or a revoked `PlatformAdmin` role end a session at its **next** request
  (ACCT-005), not whenever a cache expires. The cost is one keyed read per authenticated admin request.
- **`api/internal/v1`, a fourth API surface, is the gateway's alone.** It carries the two credential checks. The
  BFF's `/api` fallback already refuses anything that is not `/api/v1/...`, every route on the surface answers a
  plain 404 without the secret, and `ApiSurfaceConvention` now refuses to start a host whose internal-surface
  controller forgets that guard. Its OpenAPI document exists in Development only. **PP18 must keep it off the edge**
  when ApiService gets its own Front Door route.
- **The second step is bound to the first.** The half-finished ticket carries the stamp, and
  `ISessionFacade.CompleteSecondFactorAsync` checks it **before** the code and before anything is counted against the
  account. Without that, a holder of the shared secret could have turned "has a code" into "is signed in" without a
  password, and could have locked a named blogger out with wrong codes. Both are tested.
- **Sessions survive the requests that rotate the stamp.** `password/change` and `two-factor/setup|enable|disable`
  carry `[RefreshesSession]`, which returns the new stamp in `X-PP-New-Stamp`; a response transform re-issues the
  cookie against it. So the account's *other* sessions end and the caller's own carries on, which is what the plan
  promised. `recovery-codes` has no attribute: regenerating codes does not rotate the stamp.
- **A session that is over says so.** A stale stamp, a missing account, or a session that never passed a required
  second step gets 401 `session-ended` plus `X-PP-Session: ended`; the gateway clears its cookie and strips the
  header, so a person is told they were signed out instead of clicking into a wall of 401s. Neither that header nor
  `X-PP-New-Stamp` ever reaches a browser.
- **Refusals the blogger reads, and failures we own.** Only 400, 409, and 429 from the internal surface are relayed
  (with `Retry-After` when present). Anything else - a 404 from a mismatched secret, a broken API - is logged as an
  error and answered with the gateway's own 502, because "we couldn't find that" on a sign-in form is a dead end.
  The session client also has its resilience handlers **removed**: the standard handler treated the lockout 429 as
  transient, waited out the API's `Retry-After`, and turned "wait 15 minutes" into a 30-second timeout and a 500. A
  test caught it.
- **Approved defaults, as stated in the plan:** sign-in refusals are 400 `invalid-credentials`, one neutral answer
  word for word for an unknown address and a wrong password (timing equalized); a locked account gets 429
  `too-many-attempts`, which does disclose that the address exists; an unverified email can sign in (ACCT-001 gates
  publishing, not sign-in), and the session reports `emailVerified`.
- **Dev HTTPS decided (closes PP0.7's note).** The AppHost now shows only the gateway's HTTPS address at
  `app.localhost`; the plain one moves to the resource details, because a browser will not keep a `Secure` cookie
  over HTTP. `Secure` is never relaxed for Development.
- **Review fold-ins.** From `architecture-reviewer`: the stamp binding above; the "two-step sign-in must have been
  passed" rule moved out of the authentication handler into `ISessionFacade.ResolveAsync` (ARCH-002), where it is
  unit-testable and cannot be forgotten by a second surface; the relay allow-list and `Retry-After`; `Cookie`
  stripped on the Development `admin-spa` route too (a PP0.7 carry-over); a startup refusal when configuration has no
  `api-v1` route, since the whole identity layer keys on that id; the dead response-header lists now drive the
  transform; the `emailVerified` claim added to the half-finished ticket. From `api-contract-checker`: the rule and
  decision records above; the gateway's problem vocabulary completed and pinned against `ApiProblems` for every
  status, code and title alike; a test that the gateway's own `/api` endpoints are exactly the four session routes
  plus the fallback, and that the session path sits under the declared admin prefix; parity tests for the duplicated
  `SessionStart` record and the header names, with the gateway reading `SecondStepRequired` as `bool?` so a rename
  fails the sign-in instead of handing a complete session to an account that owes a second step; the internal
  document's scope, and its absence outside Development; `SessionView` moved to `PantryPress.Gateway/Contracts/`.
- **Not in this prompt:** CORS, antiforgery, and `X-Forwarded-*` (PP1.7 - this prompt pulled forward only the
  inbound `X-PP-*` and `Cookie` stripping its trust model needs); per-IP limits (PP1.8); idempotency (PP1.9); the
  admin app's sign-in screen and typed client (PP2); "remember me".

Recorded for later prompts:
- **PP1.7:** until antiforgery lands, a cross-site POST to the sign-in route is held off only by `SameSite=Lax` plus
  the JSON content type forcing a preflight nothing answers. A forced-login CSRF is the gap.
- **PP1.8:** the API's sign-in limiter is one global 300-a-minute window, so one caller can turn sign-in off for
  everyone for up to a minute, and a spray across many accounts is capped only by per-account lockout. Per-IP
  partitioning needs trusted proxies first (PP1.5's reasoning), so it stays here - with that consequence stated.
- **PP1.9:** sign-in, the second step, and sign-out take no `Idempotency-Key`. A replayed *stored* sign-in response
  must never re-issue a cookie without re-checking credentials.
- **PP1.11:** sign-out clears the browser's cookies but revokes no ticket - there is no server-side session store, so
  a stolen cookie works until it expires (8 hours) or the stamp rotates. A revocation list belongs with the audit log.
  Also: the lockout 429 still carries no `Retry-After` (PP1.3's note), while the rate limiter's does.
- **PP2:** the four session routes declare their shapes as endpoint metadata but have no OpenAPI document, so the
  admin client is written by hand for sign-in until the BFF publishes one.
- **PP19:** the gateway's data-protection key ring is neither persisted nor shared, so **the gateway must run
  single-replica** until it is: a second replica cannot read the first's cookies, and every restart signs everyone
  out. It must move to storage protected by Key Vault with ApiService's.
- **Recorded from the reviews rather than fixed:** `ResolveAsync` and `CurrentStampAsync` accept a
  `CancellationToken` they cannot pass on, because Identity's `UserManager` lookups take none; there is no catalogue
  test asserting every problem code across the five files that declare one is unique and kebab-case; the declared 400
  type on the session controller is `ValidationProblemDetails` while a refusal is a plain problem (surface-wide,
  pre-existing); `GET …/session` answers 401 `sign-in-required` on a normal cold start, which the admin app must not
  show as "you were signed out" (that is `session-ended`); and no test proves the host refuses to start without the
  `api-v1` route, only that the check is there.
- **Pre-existing, unrelated:** the `MigrationWorkerTests` pair recorded at PP1.5 still fails, verified in a clean
  worktree at HEAD (9333ff9) with this change absent: 2 failed, 2 passed, identical messages.

Verification: `dotnet build PantryPress.slnx` (0 warnings), `dotnet test PantryPress.slnx` (407 of 409 passing; the
only red is the pre-existing `MigrationWorkerTests` pair above), `dotnet format PantryPress.slnx
--verify-no-changes` (clean), and 71 new test cases: cookie attributes in Development and Production, the three
cookie-tossing shapes a browser would allow (another name, another key ring, a tenant host) and the sign-out clear;
the sign-in flow, the half-finished stage and its refusal on every forwarded route, the second step, a wrong code, an
expired half-finished sign-in, `Retry-After`, and the 502 for an answer that is not the blogger's business; identity
forwarded, a client's own identity headers stripped signed in or not, no `Cookie` forwarded on either route, the
session-ended clear and the stamp refresh; on real SQL, credentials and the second step including lockout counting,
the neutral refusal, a code without a real first step counting nothing, a stamp that moved mid-sign-in, the internal
surface's 404 without the secret, the forwarded scheme's 401s, `session-ended` with its header, roles read per
request, and the `PlatformAdmin` grant ending a running session; the cross-assembly parity tests; the internal
surface's document and its absence in Production; and the AppHost secret reaching exactly two resources. The
end-to-end half signs in through the real gateway into the real ApiService on SQL at `app.localhost` and reaches a
signed-in route. `aspire run` then proved the part no in-memory test can: every resource healthy (the gateway
reporting `Healthy` with the API reachable, the admin app served through it), and over **real TLS** at
`https://app.localhost` a sign-up and sign-in that set `__Host-pp-session` with exactly `path=/; secure;
samesite=lax; httponly` and no `Domain`, `Max-Age`, or `Expires`, cleared the half-finished cookie, reached
`/api/v1/account/two-factor` with 200 holding only that cookie, and got 401 without it. Reviewers:
`architecture-reviewer` and `api-contract-checker`; every major finding is either fixed above or recorded here.

### PP1.7 Header sanitization, CORS, and antiforgery - done

```text
SCOPE: Strip untrusted forwarding headers, set strict CORS, and require antiforgery on state-changing
requests through the gateway.
CONSTRAINT: ACCT-002; gateway.md.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*PP1.7 delivery note (done):*
- **Three checks ahead of routing, in the order an attack meets them (owner-approved plan).** Header hygiene first,
  so nothing downstream - routing, the session, the proxy's transforms - ever reads a client's claim about the hops in
  front of us; then closed CORS, which answers a preflight here; then the `Origin` check on a write; then the
  antiforgery token. All four sit in front of the endpoints, so no route can be written later that forgets them. The
  gateway still holds no rules and touches no database.
- **The gateway believes nobody about the hops in front of it.** `Forwarded`, `X-Forwarded-For`, `-Proto`, `-Scheme`,
  `-Host`, `-Port`, and `-Prefix` come off **every** inbound request, on every path, and the proxy then declares its
  own `X-Forwarded-*` with `ForwardedTransformActions.Set` rather than inheriting YARP's defaults: appending would
  hand a client's invention to ApiService as the first value of a list it had every reason to trust. The cost is
  recorded, not hidden: until PP1.8 adds the trusted-proxy step - which must run **ahead** of this strip - the gateway
  cannot see a real client address, so the per-IP rate limits gateway.md calls for have nothing to partition on yet.
- **CORS is closed on purpose, not by omission.** The default policy allows no origin, which means a cross-origin
  preflight is answered here (204, no `Access-Control-Allow-*`) and never forwarded. CORS only asks a browser not to
  *read* an answer, though, so a state-changing request whose `Origin` is not this exact scheme, host, and port - or is
  opaque, as a sandboxed frame sends - is refused outright: 403, code `cross-origin-blocked`. That enforcement was not
  in the prompt; "strict CORS" is not worth much without it, and it turns a cross-site write away on the header alone,
  whether or not it guessed a token.
- **A write under `/api/v1` carries a token the admin app could only have read from this origin.** ASP.NET Core
  antiforgery with `__Host-pp-antiforgery` (the cookie token, `HttpOnly`) and `__Host-pp-xsrf` (the request token, the
  one piece the app is meant to read), echoed in `X-PP-Xsrf`. Both cookies are `Secure`, `Path=/`, `SameSite=Strict`,
  with no `Domain`: the `__Host-` prefix is what stops a sibling tenant subdomain writing either (PSEC-003). The
  gateway's own session routes are included - a sign-in a stranger triggers is a forgery too - and the token is
  stripped before anything is forwarded, like the cookies. A read under `/api/v1` mints a pair when the browser has
  none, which is what the admin app's first call at startup does; a refusal mints one too, so a token that went stale
  for an honest reason (a cleared cookie, a gateway restart with a new key ring) costs a retry rather than a reload.
- **The token is bound to this origin, not to an account - and that took an explicit `UseAuthentication()`.** A
  `WebApplication` inserts authentication ahead of every middleware of its own accord, which silently bound each token
  to whoever was signed in when it was minted: the first write after a sign-in was then refused, because the app was
  still holding the token it read before. Calling `app.UseAuthentication()` ourselves, **after** the boundary checks,
  puts it where it belongs and leaves the token unbound, so one lifecycle rule replaces four re-minting points and no
  valid token is refused because the session underneath it moved on. What binds the token to us is the `__Host-`
  prefix, the same-origin policy that keeps another site from reading the cookie, and the `Origin` check. Nothing
  downstream changed: the session routes and the proxy read the session themselves.
- **Over plain HTTP the admin surface is read-only.** The cookie carrying the token is `Secure`, so there is nothing
  to hand out and nothing to accept: a read works, a write is refused with the same `antiforgery-failed` document
  rather than failing some other way. Nobody meets the admin app this way - the edge is HTTPS and locally it is
  `app.localhost` over TLS - and PP1.8 adds the redirect and HSTS that say so.
- **The tests now hold a browser's end of the bargain.** `GatewayFactory`'s browser carries the token the way PP2 will
  configure Angular's `HttpClient` - read it from the cookie, echo it in the header, fetch one with a read first - and
  every client it makes talks to `https://app.localhost`, because that is the only way the gateway is ever reached. The
  end-to-end half now runs over **real TLS** against the Aspire host, with the certificate unchecked because the test
  presents the `app.localhost` host while connecting to the local endpoint.
- **PP2:** the admin client must set Angular's `withXsrfConfiguration` to `__Host-pp-xsrf` and `X-PP-Xsrf`; there is no
  HTTP client in the admin app yet, so the names are written down in `gateway.md` rather than wired. The new 400 and
  403 are declared on the four session routes as endpoint metadata only, like the rest of their shapes, until the BFF
  publishes a document.
- **PP19:** antiforgery tokens are protected by the gateway's data-protection key ring, which is still neither
  persisted nor shared, so a restart invalidates every outstanding token (the re-mint on refusal is what keeps that
  from being visible) and the single-replica constraint recorded at PP1.6 now covers tokens as well as cookies.
- **Recorded rather than fixed:** a preflight's 204 comes from the CORS middleware, so it is the one answer under
  `/api` that is not a problem document - correct for CORS, and there is no body a browser would read; the two new
  problem codes live beside their middleware, as `SecondFactorRequiredCode` does, rather than in `GatewayApi`'s
  status-keyed catalogue; and an empty `Origin` header is untested because `HttpClient` will not send one.
- **Pre-existing, unrelated:** the `MigrationWorkerTests` pair recorded at PP1.5 still fails, unchanged.

Verification: `dotnet build PantryPress.slnx` (0 warnings), `dotnet test PantryPress.slnx` (452 of 454 passing; the
only red is the pre-existing `MigrationWorkerTests` pair), `dotnet format PantryPress.slnx --verify-no-changes`
(clean), and 45 new test cases: each forwarding header stripped on its own and the whole list at once, signed in and
not, on the admin route and the Development SPA route, with the API shown exactly the one value the gateway can see;
a preflight answered here with no CORS headers and nothing forwarded on both the forwarded route and a session route;
no `Access-Control-Allow-Origin` on any answer; a write refused for another origin, our host on another scheme, our
host on another port, our host as a prefix of theirs, and an opaque origin, with the sign-in case proving the API is
never asked; a write from the app's own origin and a write with no `Origin` at all going through; every unsafe method
refused without a token on a forwarded route and on all three session routes, with neither the API nor the internal
session call reached; the cookie alone, the header alone, a mismatched pair, and a pair minted by another gateway all
refused; a matching pair accepted and the token not forwarded; the bootstrap read's two cookies and their attributes
in full; no second mint for a browser that already has one; the re-mint on refusal letting the next attempt succeed;
one token surviving a sign-in and a sign-out; reads needing nothing; plain HTTP read-only; `/api/publishing/v1` still
answering PP1.5's 404 rather than a token puzzle; the SPA route not guarded; and the guarded path pinned against the
forwarded route's configured path. Reviewers: `architecture-reviewer` and `api-contract-checker`.

### PP1.8 Edge hardening - done

```text
SCOPE: Add security response headers, body size limits, and trusted-proxy configuration to Gateway,
ApiService, and Site.
CONSTRAINT: PSEC-002 (admin side); backend.md.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

- **One shared layer, opted into per host.** `PantryPress.ServiceDefaults/EdgeHardening/` holds the four pieces -
  security headers, trusted proxies and the forwarding strip, body limits, the HTTPS redirect - because Gateway,
  ApiService, and Site owe the same primitives and ServiceDefaults is the only project all three reference. What each
  one owes *differs*, though, so nothing is applied by `AddServiceDefaults()`; each host says what it wants and the
  differences are the interesting part. CLAUDE.md's layout line and `aspire.md` were widened to say so (approved).
- **The trusted-proxy step is what makes PP1.7 work in production, not just what per-IP limits were waiting on.**
  Behind Front Door, TLS ends at the edge: `Request.IsHttps` is false and `Request.Host` is the container's, so
  antiforgery would refuse every write as plain HTTP and the same-origin check would refuse it as cross-origin. The
  step runs **ahead** of PP1.7's strip, reads `X-Forwarded-For`, `-Proto`, and `-Host` from a peer listed in
  `Edge:TrustedProxies`, and the strip then takes whatever is left off every request. It is **empty by default**, and
  empty means the middleware is not registered at all rather than configured to trust nothing - there is no
  ambiguity about what an empty allow-list means. `ForwardLimit` counts the believable hops; the tests prove a known
  peer is believed, a stranger saying the same thing is not, and two hops do not get through one.
- **The strip grew the families PP1.8 actually started reading.** `X-Azure-FDID`, `X-Azure-ClientIP`,
  `X-Azure-SocketIP`, `X-Original-Host`, `X-Original-URL`, `X-Real-IP`, `X-ProxyUser-Ip` - `X-Azure-FDID` above all,
  because PP9.3 proves a request came through Front Door with it and a client that could forge one would walk past
  that check before it was written.
- **ApiService believes an address behind the secret, not an allow-list.** Pod addresses are not knowable in advance;
  the gateway's shared secret already is the thing this service believes an identity on (PP1.6), so it is what it
  believes an address on too. Any other caller - the publishing API's own edge route at PP18 - has its forwarding
  headers removed and gets no say. The forwarded *host* is deliberately not applied: no admin route reads it, and
  the one place a host decides anything is the public tier, where PP9.3 owns the rule.
- **The public site believes nobody at all** (PSEC-004). Its site resolution is Host-only, and proving a request came
  through the edge rather than straight at the origin is PP9.3's; until then the strip is the whole rule.
- **The headers are written on the response's start**, so the answers that never reach an endpoint - a 413, a
  cross-origin 403, an antiforgery 400, a problem 404 - carry them too, which is exactly when a missing header would
  matter most. `X-Frame-Options: DENY` here is what PP1.7's `SuppressXFrameOptionsHeader` was making room for. An
  `/api` answer gets `default-src 'none'; … sandbox`, because JSON is not a document; a document gets the admin
  app's own policy, looser in Development for the dev server's sake and tightened by PP19 with a nonce.
- **Where the surfaces differ, they differ for a reason.** `Referrer-Policy` is `no-referrer` on the admin side and
  `strict-origin-when-cross-origin` on the public site, because a blogger's outbound links are credited back by the
  referrer. `Permissions-Policy` denies everything except `screen-wake-lock=(self)` on the public site, for cook
  mode. HSTS is one year with `includeSubDomains`, **no `preload`** (a one-way door owned by browser vendors), on
  the gateway and the public site alone and outside Development only, so no developer's browser is pinned. Only the
  gateway redirects plain HTTP to TLS, with 307 so a write keeps its method and body: the internal APIs are reached
  over plain HTTP inside the environment, and a redirect there would break the system rather than harden it.
- **Body limits are refused on the declared length, before anything reads one.** 1 MiB on the admin side, 64 KiB on
  the public side, where everything a reader sends is small by rule. Early on purpose: the gateway would otherwise
  stream an over-sized body to ApiService and turn a 413 into a 502, and an antiforgery check that parses a body is
  work an anonymous caller must not be able to ask for.
- **The gateway answers its own host check.** The framework's host filtering would do this with a bodiless 400,
  which would be the one answer under `/api` that is not a problem document, so a host outside `Gateway:Hosts` gets
  the gateway's neutral problem 404 instead - a tenant site host included, since Front Door routes those elsewhere.
  `Gateway:PublicOrigin`, when set, is what the same-origin check compares against, so that check stops depending on
  proxy configuration being right (an earlier PP1.7 review's suggestion).
- **The deferred limit landed** (approved as part of this prompt; PP1.5, PP1.6, and PP1.7 each recorded it here).
  The gateway limits its two sign-in routes per address, 20 a minute by default. ApiService partitions its anonymous
  account routes by the address the gateway vouches for (`AnonymousAccountPerIpPerMinute`, renamed from
  `AnonymousAccountPerMinute` so the name says what it means), and its sign-in ceiling stays **global on purpose**:
  only the gateway reaches that surface, it calls with a client of its own rather than through its proxy, and the
  gateway already partitions sign-in by address in front of it. That closes PP1.6's note that one caller could turn
  sign-in off for everyone.
- **PP1.7 fold-in (approved):** a write with no `X-PP-Xsrf` header is refused before `ValidateRequestAsync` is
  called. ASP.NET Core's token store falls back to the posted form for a form content type, so a token-less
  multipart write would otherwise have its whole body parsed - and a malformed one throws `InvalidDataException`,
  which escapes the antiforgery `catch` and makes a 500 of a documented 400.
- **Three existing tests changed, and one deliberately lost its old meaning.** PP1.7's "over plain HTTP the admin
  surface is read-only" is now "over plain HTTP nothing is served": the redirect answers before the token question
  arises, and the scheme guard behind it is a backstop rather than something a caller meets. Two integration tests
  moved from the gateway's http endpoint to its https one, and the tenant-host cookie test now ends at the host
  check rather than at the ticket (that a ticket this gateway did not write names nobody is still proven on its own
  host, in the test above it).
- **The reviews changed the shipped design in four places, and all four were worth it.**
  - *A deployed host now refuses to start trusting nobody.* Everything the empty `Edge:TrustedProxies` default costs
    is invisible locally, because Kestrel terminates TLS itself in a local run - so an unconfigured production would
    have had the per-address limits collapse into one bucket **tighter** than the ceilings they replaced (20 a
    minute for every caller on earth, where PP1.6 recorded 300), no HSTS, and every admin write refused. It says
    who its proxy is or sets `Edge:TrustedProxies:TrustNobody` in writing.
  - *The redirect is registered only where the scheme is knowable* - Development, or a deployment that named a
    proxy. An HTTPS port configured without one would answer 307 to the address the edge had just come from, and
    the admin app would be an infinite loop. The two are coupled in code rather than in two lines of IaC that have
    to agree.
  - *HSTS on the public tier drops `includeSubDomains`.* DOM-001 allows a custom domain at its apex, and a year of
    it from `example.com` pins `mail.example.com`, `shop.example.com`, anything the blogger runs - on a domain that
    is not ours, not undone by leaving PantryPress. The gateway keeps it; its host is ours.
  - *Believing a proxy about the host became a separate decision from believing it about an address.*
    `TrustedProxyOptions.ForwardHost` is off by default and the public tier leaves it off: on that tier the host is
    the tenant (PSEC-004), and one CIDR in configuration would otherwise have made tenant selection header-settable
    with no test turning red. The gateway turns it on and bounds it to `Gateway:Hosts`.
- **Also from the reviews:** the public tier's 413 was a bare framework document with no `code` - the shared
  middleware now states the vocabulary itself, so a host whose own problem vocabulary has not been written yet
  (PP2, PP7) still answers a document a person can read; the 413 is written through `Results.Problem` rather than
  `IProblemDetailsService` directly, because that service declines to write when the caller's `Accept` names
  neither JSON nor `*/*` - a browser form post, which is exactly the shape of an over-sized upload - and the caller
  would have got a bodiless 413; 413 is declared globally on ApiService beside 429 and 500 and on the gateway's
  session group, since the cap sits ahead of every route; `Gateway:PublicOrigin` is compared as an origin rather
  than as the string someone typed; the Privacy Sandbox advertising features are denied alongside the device ones,
  which is the consistent position for a platform whose analytics are cookie-free and whose ads are opt-in; and the
  local host names moved out of the deployed `Gateway:Hosts`.
- **Recorded rather than fixed:** a chunked body that declares no length is enforced by Kestrel only when something
  reads it, which for a forwarded request happens inside YARP, so an over-sized chunked write surfaces as a 502
  rather than a 413 - real clients send `Content-Length`, and PP6 is where chunked uploads become real; the body
  cap is one number per host and cannot be raised per route, which PP6 (media) and PP18 (publishing uploads and
  larger publish payloads) will each need, and both will have a route to hang it on and a test to prove it;
  `HTTPS_PORT=443` is PP19's to set alongside the trusted peer, not instead of it; nothing enforces the middleware
  order the comments insist on, so a future host could reverse the trusted-proxy step and the strip and still
  compile (an ordered composite would fix it); where the gateway forwards, a security header ApiService set itself
  is what the browser gets, because the writer does not overwrite - the values agree today and a test now pins
  which wins; ApiService hand-rolls a small forwarding parser beside the framework-based one, because its trust
  gate is a secret rather than an address; the public origin has no rate ceiling of its own and no deliberate cache
  headers yet (PSEC-001, PP9.4); the placeholder public CSP still lacks `frame-src`, `media-src`, `worker-src` and
  a reporting endpoint, and `fullscreen=(self)` will block fullscreen on an allow-listed video embed, all of which
  PP9.6's generator must handle; and both health endpoints short-circuit the moment they are routed, ahead of every
  edge middleware, so a probe's answer carries no security headers and meets no host check (correct, and the
  exemptions in the redirect and the host check are belt to that braces).
- **Not in this prompt:** Front Door configuration, `X-Azure-FDID` origin trust and WAF rules (PP9.2, PP9.3), the
  generated per-site CSP (MON-005, PP9), media upload limits (PP6), Worker hardening (it has no public surface),
  idempotency (PP1.9), and the PP1.7 contract backlog an earlier review raised - the missing `ProducesProblem`
  metadata on the session routes above all, which is a separate fix-up and is **not** closed by this prompt.
- **Pre-existing, unrelated:** the `MigrationWorkerTests` pair recorded at PP1.5 still fails, unchanged.

Verification: `dotnet build PantryPress.slnx` (0 warnings), `dotnet test PantryPress.slnx` (514 of 516 passing; the
only red is the pre-existing `MigrationWorkerTests` pair), `dotnet format PantryPress.slnx --verify-no-changes`
(clean), and 62 new test cases: every header pinned by exact value on a forwarded answer, a problem 404, a session
route and the SPA catch-all, and on each refusal that never reaches an endpoint; the API policy against the document
policy, and the Development document policy against the strict one; HSTS present outside Development, absent
locally, absent over plain HTTP, and without `includeSubDomains` on the public tier; the redirect preserving path,
query and method, skipping both probes, and not registered at all where the scheme cannot be known; a known peer
believed about the address and the scheme, a stranger and an unconfigured host believed about neither, one hop out
of two, a peer bounded to the gateway's own host list, and the plain-HTTP write that only works because of that
step; each rewriting-proxy header stripped on its own, the Azure family included; a foreign host, a suffix attack, a
trailing dot and an uppercase spelling against the host check, with a probe answering on any host; the 413 refused
on declared length with its code, title, `traceId` and `no-store`, nothing forwarded, a body at the limit passing,
and a refusal that still has a body for a caller sending `Accept: text/html`; sign-in limited per address with
`Retry-After` and the refused attempt never reaching the API, and a second address with its own allowance;
ApiService's address believed from the gateway's secret and not from a caller without it; the deployed host that
refuses to start rather than trust nobody by accident; the shared rule on its own small host, proving that believing
a peer about an address is not believing it about a host; and on the public tier the reader headers, the API policy
on a reader endpoint's path, the body cap at and over the limit, and four tenant host spellings answered alike. The
end-to-end half runs against the real AppHost: real Kestrel refusing an over-sized declared length with the API's
problem shape, the headers on a real TLS answer, the neutral 404 for a foreign host, plain HTTP redirected, and the
probe that is not. Reviewers: `architecture-reviewer`, `api-contract-checker`, and `public-site-reviewer`; every
blocker and major finding is either fixed above or recorded here.

### PP1.9 Idempotency foundation - done

```text
SCOPE: Add an idempotency-key store and middleware for unsafe requests that opt in, returning the stored
result on replay.
CONSTRAINT: POST-005, API-005; backend.md.
RESTRICTION: Keys are scoped to the caller and expire (DECIDE 24 hours).
BEHAVIOR: Plan, wait for approval, implement with replay and concurrency tests.
```

- **DECIDE closed at its default:** a key is remembered for **24 hours** (`Idempotency:Retention`). An unfinished claim
  holds the key for a **5-minute lease** (`Idempotency:Lease`) before a retry may take it over; a retry meanwhile gets
  409 with `Retry-After: 1`, because the request holding the key usually finishes in well under a second.
- **Opt-in per write, and only where it is safe.** `[Idempotent]` (optionally `Required = true`) on an action. The host
  refuses to start when it sits on a read, on a route without a signed-in caller (keys are scoped to the caller -
  `account:{id}` today), beside `[AnswersWithSecret]` (new; on two-factor `setup`, `enable`, and `recovery-codes`),
  beside `[RefreshesSession]`, or on the gateway's internal surface. Controllers only: a minimal endpoint would not be
  checked, and ApiService maps none under `/api`.
- **The store** is one SQL table in the shared kernel (`Managers/Idempotency`; migration `IdempotencyRecords`, purely
  additive), primary key (`CallerScope`, `Key`) in a binary collation so keys are case-sensitive. Not site-owned data
  (PP-D06): the caller isolates it, and the fingerprint - SHA-256 over caller, key, method, path and query, and body -
  carries the site through the path. A claim is an insert, so concurrent first requests cannot both win; takeover,
  completion, and release are conditional on the lease token, so a request whose claim was taken over can change
  nothing. The store uses its own context, never the request's, and recognises its own row when the connection's
  retry policy re-sends an insert that did land; an insert that failed for any reason but a race is thrown, not
  retried.
- **The middleware** runs after authentication, authorization, and the rate limiter. A replay is the stored status,
  body, and allow-listed headers (`Content-Type`, `Location`, `ETag`, `Content-Language`) plus `Idempotent-Replayed:
  true` - never a cookie or the gateway's session headers. Stored: every answer below 500 but 401, 408, and 429, so a
  refusal is replayed and the client sends a new key after fixing the cause. An opted-in write **runs to its end even
  when the caller goes away**: the request's abort token is withheld from the route and a gone caller's stream stops
  being written to while the copy carries on, so the retry of a lost answer gets the whole answer. That was a review
  finding against the first cut, which would have released the key or stored a cut-short body; a test simulating the
  disconnect proves it, and fails when the fix is taken out. Answers over 1 MiB, or with headers over 4,000
  characters, are not stored, and the key is released.
- **Problems:** 400 `idempotency-key-invalid` and `idempotency-key-required`, 409 `idempotency-key-in-use`, 422
  `idempotency-key-reused`, following the IETF Idempotency-Key draft. OpenAPI documents the header (with its pattern
  and the configured retention), `Idempotent-Replayed` on success, `Retry-After` on the 409, and the 400/409/422
  answers where a route or its controller has not declared its own. The gateway passes the key and the marker through.
- **Seam reconciled:** `backend.md` and `architecture.md` gave idempotency to the facade. Key replay is now stated as
  host plumbing ahead of MVC, and the facade keeps making its own work safe to repeat. `api-contract.md` holds the
  header's rules; the decisions record closes the DECIDE item.
- **Not delivered from the PP1.3 backlog (above):** `password/change`, `password/reset`, and `two-factor/enable` were
  recorded as opt-in targets. None of them can be one. `reset` is anonymous, so it has no caller to scope a key to;
  `change` and `enable` rotate the security stamp, so the retry of a lost answer carries the old stamp and is refused
  as signed out before the key is read, and a replay could not carry the new stamp anyway; `enable` also answers with
  recovery codes. The obligation moves to the facades: make each safe to repeat (a `change` whose new password is
  already the current one succeeds; a `reset` link already used by the same request reports success). **Open.**
- **Recorded for later prompts:**
  - **PP1.10 or the next jobs prompt:** schedule `PurgeExpiredAsync` from the Worker. Until then expired records are
    ignored and overwritten but never deleted. **This must ship before or with the first route that opts in**, because
    stored answers will then hold site data.
  - **PP2:** site membership must be decided by an authorization policy ahead of the idempotency step - a replay never
    reaches MVC filters or the facade, so a membership check there would let a replay outlive a revoked membership for
    24 hours. Put the resolved `SiteId` into the scope or the fingerprint (tenancy.md rule 8: the path's slug can be
    renamed), which needs `CallerScope` widened past 80 characters; consider not storing 403 and 404 once refusals
    depend on membership; give site deletion a way to remove these records; and add the two-site cases - one account in
    two sites with one key (422, nothing created in B), membership revoked inside the retention (404, not a replay), a
    renamed slug, a suspended site.
  - **PP18:** a publishing key is a caller of its own (`publishing-key:{siteId}:{keyId}`), never the Owner's account,
    and the convention should check that every opted-in route's scheme yields a scope. Decide `Required` for the
    publishing surface before its first release (changing it later is breaking) and enforce it at startup for every
    write there. Key creation is `[AnswersWithSecret]`. Two keys of one site never replay each other.
  - **Before opting in any route with reader data:** stored answers are not encrypted at rest (PSEC-005); such a route
    stays out until they are.
  - **Atomic completion:** a crash after a facade commits but before the answer is stored re-runs the write once the
    lease ends. Closing it means the facade completes the key in its own transaction.
- **Minor, recorded rather than fixed:** EF logs an error for each insert that loses a race (one line per losing
  concurrent first request); the clustered key on random strings fragments, which a short-lived table tolerates;
  rolling the migration back drops in-flight keys, so retries in that window run again.
- **Local note:** an `aspire run` started during this prompt applied the `IdempotencyRecords` migration to the
  persistent local database. It is additive; nothing to undo.

Verification: `dotnet build PantryPress.slnx` (0 warnings; built to a separate artifacts path, because a running
`aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test
PantryPress.slnx` (587 of 588; the one red, `AdminAppTests.Gateway_serves_the_admin_app_and_keeps_its_own_health_endpoint`,
timed out waiting for the admin dev server while another AppHost was running, and passes on its own). The PP1.5
`MigrationWorkerTests` pair passed in this run. New: 18 API cases against the migrated schema - replay, a different
body or path, ten retries held off while the first runs and twenty racing first requests that run once, two callers
with one key, the 24-hour boundary, a lease taken over with the late finisher changing nothing, a disconnect mid-request
whose retry gets the whole answer, a replay that still needs a signed-in caller, failures released, refusals replayed,
the required and malformed key, quoted and case-sensitive keys, no cookie or session header replayed, and an over-sized
answer not stored; 6 store cases racing claims and takeovers for real against SQL Server, stale leases, release, and the
purge; the rules, key parsing, the salted fingerprint, and which statuses are kept; the startup refusals (a read, an
anonymous or unguarded route, a secret, a session rotation, the internal surface); the OpenAPI header, pattern, response
headers, and a controller's own 400 kept; and the gateway passing the key and the marker through. Reviewers:
`architecture-reviewer`, `api-contract-checker`, and `site-isolation-auditor`; every blocker and major finding is either
fixed above or recorded here.

### PP1.10 Transactional outbox - done

```text
SCOPE: Add a transactional outbox and dispatcher in the Worker with at-least-once delivery and idempotent
handlers.
CONSTRAINT: backend.md; Template G.
BEHAVIOR: Plan, wait for approval, implement with crash and replay tests.
```

*Split (owner-approved 2026-10-24):* building the outbox and moving email onto it at once would be a schema, a job,
provider wiring in a new host, and every Accounts facade. So it runs as one seam each: **PP1.10a** the outbox store,
the Worker's dispatcher, the migration, and the Worker's database wiring, proven with test handlers; **PP1.10b** account
email onto the outbox (`IEmailQueue` stages an `email.send` message whose id is the email's id, facades stage it before
their save, the Worker hosts the email gateway and the development mail sink, and the in-process queue goes). PP1.10 is
marked done after PP1.10b. The owner also approved the Worker purging expired idempotency keys here (PP1.9's hand-off).

*PP1.10a delivery note (done):*

- **Writing.** `IOutbox` (scoped, `Managers/Outbox`) stages a message on the request's own context, so it commits with
  the facade's save, or its transaction, and is lost with it. A message type implements `IOutboxMessage` with a stable
  `OutboxType` (lowercase dot-separated, owner first) instead of its CLR name. It carries ids, not data: at most 64 KiB
  of JSON, and never a site id of its own. A type with a `SiteId` property is refused when it is enqueued or a handler is
  registered for it. `siteId` is a required argument, so platform work is `null` said out loud. ApiService registers
  `IOutbox` only; the Worker alone registers the store and the dispatcher.
- **The table** `OutboxMessages` comes from the additive migration `Outbox` (Template K reviewed: one table, two
  filtered indexes, no data touched). It is not site-owned data (PP-D06). The dispatcher reads every site's messages,
  so there is no global filter and no `IgnoreQueryFilters`. No route reads the table. A boundary test keeps every Domain
  file outside `Managers/Outbox` from naming it.
- **State machine.**
  - Pending → claimed: the lease is set, the claim takes an attempt count snapshot, and nothing is charged yet.
  - Claimed → started: the lease is renewed from now and the attempt is charged before the handler runs. A claim whose
    lease was taken over while it waited in the batch is not started, costs nothing, and is left to the Worker that
    took it.
  - Started → Succeeded: the payload is cleared. Or → retry with exponential backoff jittered over its upper half
    (5 s doubling, capped at 30 min). Or → Poisoned on `OutboxPermanentFailureException`, an unreadable or missing
    payload, the last of 12 attempts (roughly one to two hours), or a claim past the last attempt (a message that kills
    the process runs out instead of looping).
  - A stopping Worker hands its messages back due now. One that never started costs nothing; a handler it cut short
    keeps its charge, so a handler that hangs until every stop still runs out.
  - Every write after the claim is conditional on the lease token, so a late finisher records nothing. The handler is
    cancelled a tenth of the lease before the lease ends.
- **Competing Workers, and types a Worker does not know.** The claim is one conditional update over freshly read
  candidates, so of two Workers reaching for one row exactly one gets it. A Worker claims only the types it has
  handlers for, so during a rolling deploy an old Worker leaves a new type to the new one instead of poisoning it. A
  type nobody handles waits, visible in the `pantrypress.outbox.ready.age` gauge (the OPS-004 backlog alert). Once its
  update is sent, a claim runs to its end, so a stop never strands leased rows.
- **Telemetry.**
  - The `PantryPress.Outbox` meter counts delivered, retried, poisoned, and released messages, tagged with the type
    only, and records handler duration and ready age.
  - The consumer span joins the writer's trace through the stored `traceparent`. ServiceDefaults registers the
    `PantryPress.*` meters and sources.
  - Logs carry ids, types, and a failure's description (its exception type, or a permanent failure's stated reason),
    never a handler exception's text. A test proves an email address in a handler's exception reaches no log.
- **The sweep** runs every minute on the Worker. It samples the ready age and purges, in batches, delivered rows after
  7 days, poisoned rows (payload kept for an operator) after 30, and idempotency keys past their retention.
- **Test harness.** The Worker now sweeps on the real clock, so the two fixtures that move a fake clock over a shared
  database (`MigratedDatabase`, `AccountApiFixture`) start their AppHost without it (`EphemeralAppHost(runWorker:
  false)`). Otherwise it would purge keys their tests still use.
- **Out-of-scope repair:** `EdgeHardeningEndToEndTests.The_real_gateway_hardens_its_answers` (PP1.8) failed in two of
  three full runs. Its over-sized body raced the gateway's refusal, and the client saw the reset instead of the 413. It
  now sends `Expect: 100-continue`, which makes it deterministic and proves the body was refused unread.
- **Deferred, for named prompts:**
  - **PP1.10b:** email onto the outbox. Atomicity there needs each facade to stage the message immediately before its
    one save. Identity's `UserManager` shares the context and saves on its own, so a message staged before a failed step
    could ride a later save.
  - **PP2.4/PP2.5:**
    - The writer's site comes from `ISiteContext`, not an argument. A mismatch throws, and platform work gets an
      explicit path that refuses while a site context is set. Switch to the typed `SiteId?`; the column is unchanged.
    - The dispatcher, not each handler, re-resolves the site in each delivery's scope before invoking: it loads the
      site, finishes or poisons quietly if it is missing or `Deleted`, and sets the context. A platform message gets a
      context that matches nothing.
    - The global filter keys on `ISiteOwned`, never on a property named `SiteId`, which would starve the outbox. Record
      `OutboxMessage` beside `Site` as a written exception.
    - Two-site tests through the PP2.7 harness: a mismatched-site enqueue, parallel siblings with separate contexts, and
      a platform message that sees no site rows. Today's two-site test proves only that sites' messages stay separate,
      carry their own site, and fail alone. (PP2.7: the harness is ready; these wait for the Worker's site resolver and
      belong to the first site outbox message.)
  - **PP7 and the import prompts:** per-site fairness in the claim (round-robin or a per-site cap). Today the claim is
    oldest-first across sites, and competing Workers reach for the same candidates. The losers' partial claims cost
    throughput, not correctness; an `UPDATE TOP … READPAST … OUTPUT` claim is the upgrade.
  - **Handler idempotency kit.** There is no shared processed-message table yet. The contract on `IOutboxHandler` is
    that an external effect passes the message id as the provider's idempotency key, and a database effect records it in
    its own transaction or is safe to repeat. The first database-effect handler that cannot be naturally idempotent adds
    the kernel table.
  - **PP19:** an operator view and replay for poisoned messages, an alert on `pantrypress.outbox.poisoned`, and the
    ready-age alert.
  - **Site and account erasure (OPS-011):** delete outbox rows by `SiteId`, with an index then. Payloads stay
    references, not personal values; PSEC-005 covers reader data.
  - **PP9:** re-root an inbound `traceparent` on the public tier before any reader write enqueues.
- **Minor, recorded rather than fixed:**
  - The clustered key is a random GUID on a churning table, which fragments it, as PP1.9's does.
  - Leases are timed by the Workers' clocks.
  - A completion re-sent by the retry policy after it landed logs a spurious "lost its lease".
  - Message ids derived from site-local values must include the site, or two sites collide; this is documented on
    `IOutbox`.
- **Local note:** an `aspire run` that restarts its migration service will apply the additive `Outbox` migration to the
  persistent local database. Nothing to undo.

Verification:

- `dotnet build PantryPress.slnx` gave 0 warnings and 0 errors. It was built to a separate artifacts path, because a
  running `aspire run` held the default outputs. The tests ran with the same path as `UseArtifactsOutput` and
  `ArtifactsPath` environment variables, so the projects the test AppHost launches run the fresh build.
- `dotnet format PantryPress.slnx --verify-no-changes` is clean.
- `dotnet test PantryPress.slnx` passed 645 of 645, twice in a row. One earlier full run had 26 account-API tests
  answer 500 under parallel AppHost load; they passed alone and in both final runs.
- New tests:
  - 22 outbox integration cases against the migrated schema, with the production retrying execution strategy: commit
    with the change, a failed save, a rolled-back transaction, delivery and payload clearing, trace continuity, crash
    and redelivery with the late finisher refused, a claim taken over before it started, the lease renewed after
    waiting in the batch, a poison pill, a handler outliving its lease, backoff, poisoning at the last attempt with the
    payload kept and nothing personal logged, permanent failure, unreadable and missing payloads, 200 messages across
    three competing Workers each delivered once, 60 redeliveries with the same id and no repeated effect, a stop that
    charges only the cut-short delivery, the hosted dispatcher, an unhandled type left waiting with the backlog gauge,
    two sites, and the sweep.
  - Unit tests: the backoff table, attempts, type names, stored descriptions, settings refusals, staging and its
    refusals, the site-carrying message type, duplicate handlers, the no-handler Worker, and the hosted registrations.
  - The boundary test above.
- Reviewers: `architecture-reviewer` and `site-isolation-auditor`. Every blocker and major finding is either fixed above
  (the lease charged at the claim, exception text in logs, stranded claims, the head-of-queue row, the free `siteId`
  default, site ids in payloads) or recorded here.

*PP1.10b delivery note (done at PP1.12, owner's "fix all", 2026-10-24):* account email is on the outbox, and the
in-process queue is gone (`IEmailQueue`, `InProcessEmailQueue`, and ApiService's `ApiEmail`).

- **Differs from the approved split, deliberately.** The plan had `IEmailQueue` stage an `email.send` message carrying
  the email. What shipped is `accounts.email` (`AccountEmailMessage`, Accounts-owned): the account id, the kind of email,
  when it was asked for, and - for a reset - a one-way fingerprint of the security stamp. The Worker's
  `AccountEmailHandler` reads the address and makes any link when it sends, so no address and no live reset token ever
  sits in `OutboxMessages`, where a poisoned row keeps its payload for 30 days. A link email that waited longer than its
  link lives, or a reset whose account's stamp has moved since, is not sent (the person asks again).
- **Staging.** Each facade stages its email immediately before its one save; sign-up stages the first confirmation email
  in the same save as the account. A save that loses a race drops the staged message with the change, and a dropped
  resend or reset is logged.
- **Delivery.** The email's id is the outbox message id, sent as `Message-ID`, so a redelivery is recognizable; it does
  send again, the one written exception to "idempotent on the message id" (`backend.md`). A refused recipient, or an
  address no email can be written to, is poisoned at once; a transient failure is retried with the outbox's backoff.
  The provider call goes through `AccountMailDataLayer` (ARCH-002).
- **Hosts.** ApiService sends no email and needs no link origin. The Worker registers the email handler only where it
  has a provider - local Development, the mail sink - and otherwise claims no account email at all: messages wait,
  visible in the backlog gauge, `/health` reports `account-email` degraded, and the rest of the Worker runs. PP1.2a's
  "refuse to start without a provider" became that, because a Worker that refuses to start takes the dispatcher and the
  sweep down with it. A Worker that would send outside Development without a persisted key ring refuses to start: it
  makes the links ApiService checks. The AppHost and the Terraform moved `Accounts__AppBaseUrl` and the mail sink to the
  Worker and gave it ApiService's key ring; the gateway's ring is now a container and key of its own.
- **Recorded for later prompts:** PP19 must persist ApiService's key ring, shared with the Worker, before PP13.1 sends
  deployed email (PP13.1's text now says so). The recovery-code count in its email is read when it is sent, so two codes
  used in quick succession both report the later count.

### PP1.11 Audit log - done

```text
SCOPE: Add an append-only audit log for security-relevant and operator actions, with actor, site, action,
target, and timestamp.
CONSTRAINT: ACCT-005, OPS-006; backend.md.
RESTRICTION: No personal data beyond what the action needs. No content bodies.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*PP1.11 delivery note (done):*

- **Writing.** `IAuditLog` (scoped, `Managers/Audit`) stages an entry on the request's own context, exactly as the
  outbox stages a message. The facade records it immediately before its one save, so the entry commits with the change
  or is lost with it - a failed save, a rolled-back transaction, and a lost concurrency race all leave nothing (tested).
  A repeat that changes nothing records nothing. Each module owns its action codes in its contracts (`AccountAudit`);
  the kernel only checks their shape. `AddAccounts` and `AddAccountRoles` register the log themselves, so no host can
  forget it.
- **The record (owner-approved).** `AuditEntries`: an identity `bigint` key (append order, no GUID fragmentation), the
  time from `IClock`, the action code, the actor kind (`account`, `operator-configuration`, `system`) with the account's
  id for an account, an optional site, a target kind and id, an optional reason, and the W3C trace id. Ids and codes
  only: no email, name, IP address, user agent, request body, content, or free-form details. A target id is letters,
  digits, and `. _ : -`, so an email cannot be one, and a whole publishing key (`pp_live_…`) is refused. A reason is at
  most 256 characters and refused if it contains an at sign, checked on the NFKC form so full-width ones count.
- **Append-only, three layers.**
  - The API has no update or delete.
  - `PantryPressDbContext` refuses to save a modified or deleted entry, including one edited in place.
  - The `TR_AuditEntries_AppendOnly` trigger refuses every update and delete in the database, which covers raw SQL and
    `ExecuteUpdate`/`ExecuteDelete`. EF Core is told about the trigger (`HasTrigger`).
  - The deployed app logins are `db_datareader`/`db_datawriter`, so they cannot drop or disable the trigger; only the
    migration job is `db_owner`.
- **Events.**
  - Account (actor is the account itself): password changed, password reset (the link proves who), two-step on, two-step
    off, recovery codes replaced, recovery code used.
  - `account.locked-out`, actor `system`: only the wrong password or code that locks the account. The attempts before it
    are not audited, and nor is a refused attempt on an account that is already locked.
  - `account.platform-admin-granted` and `-revoked`, actor `operator-configuration`, with the reason. These are named
    `account.*` rather than the planned `platform.*` because the Accounts module records them and the account is the
    target.
  - Not audited: successful sign-ins and authenticator codes, which stay in logs and telemetry (owner-approved), and
    setting up an authenticator key, which turns nothing on.
- **PlatformAdmin atomicity.** `IAccountRoles.Grant/RevokePlatformAdminAsync` now take the actor and reason, so
  `AccountRolesFacade` records the entry in the same save as the role change and the new security stamp. The entry is
  validated before anything is staged, so a refused reason cannot leave a role change on the context (tested).
  `IPlatformAdminFacade` takes a typed `AuditActor`; its structured log lines stay.
- **Tenancy.** The table is not `ISiteOwned` and has no filter, like `OutboxMessages`: only the operator reads it, across
  sites (OPS-006). `tenancy.md` now names both tables as written exceptions. `SiteId` is nullable and passed out loud,
  `Guid.Empty` refused, and every PP1.11 event records `null`. The `(SiteId, Id)` index is filtered to non-null sites;
  `(TargetKind, TargetId, Id)` and `(ActorId, Id)` serve the console's "find by account". A boundary test keeps every
  source file in every project, apart from `Managers/Audit` and the migrations, from naming the type or the table, so a
  host cannot read it with raw SQL either. A probe proved the test fails on such a file; its first version could not
  fail, because a scripting slip left backspace characters in the pattern, and that is fixed.
- **Migration** `AuditLog` (Template K reviewed by `architecture-reviewer`): one table, three indexes, and the trigger.
  It is additive, with no backfill, no foreign keys, and no locks on existing tables. Its rollback refuses while the
  table has rows, because `DROP TABLE` fires no delete trigger and would silently erase the trail. The guard is not
  covered by a test: rolling back the shared test database would break the other fixtures. Deployment: the migration
  job must finish before an API revision that records entries serves traffic.
- **PP1.4 fold-in.** The reconcile rule moved into `Business/PlatformAdminReconciliation` as a pure function. The
  reconciler keeps its throws, with the reason written beside them: its only caller is the migration job, whose
  non-zero exit is what keeps dependents from starting on a half-applied list.
- **Declined from review, with reasons.** Detaching a staged entry when a save fails. The entry stays tracked beside the
  change it describes, and that is deliberate: detaching only the entry would let a later save on the same scope commit
  the change unaudited. `IAuditLog` documents the outbox's rule instead: a caller that gives up on a save gives up on
  the scope.
- **Not in this prompt:** reading the log, routes, or the console (PP19); retention, purge, and erasure (OPS-011); the
  publishing-key actor (PP18); taking the site from `ISiteContext` (PP2.4); the sign-out revocation list from PP1.5's
  note, which is a server-side session store and its own seam (PP1.12 should place it); lockout's `Retry-After`;
  `DENY UPDATE, DELETE` for the app logins (IaC).

Recorded for later prompts:
- **PP2.4:**
  - Take the site from `ISiteContext`, refusing a `siteId` that differs from the context, and `null` for a `site.*`
    action. Keep an explicit "no site" path, so account and platform actions never inherit a request's site.
  - The global filter keys on `ISiteOwned`, never on a `SiteId` property: `AuditEntry` sits beside `OutboxMessage` as a
    written exception.
  - Two-site tests: a mismatched-site record refused, and provisioning's `site.created` recorded with the new site.
  - The `SiteId` stamping interceptor runs after the save guard; check the guard still sees the final tracked state.
- **PP18:**
  - A publishing-key actor kind. `ActorId` is read by `ActorKind`, so `IX_AuditEntries_Actor` becomes
    `(ActorKind, ActorId, Id)` in that prompt's migration, and the key's stored id is what is recorded.
  - Every publishing-API write is audited (PP18.9) with a two-site test using matching slugs.
- **PP19:**
  - The one reader lives in `Modules/Platform/`, behind `PlatformAdmin` with two-step sign-in, and is declared as the
    boundary test's exception.
  - Reads of site-scoped targets filter on `SiteId`, or record globally unique ids (a `PostId`, never a slug), so two
    sites' `post:same-slug` never mix.
  - Operator reasons arrive as input: validate them with FluentValidation at the facade and return a plain validation
    error, rather than letting `AuditRules` throw.
  - `TraceId` helps find logs but is not evidence: nothing re-roots an inbound `traceparent` at the gateway yet (PP9 does
    it for the public tier).
- **OPS-011:**
  - Decide whether pseudonymous `ActorId`, `TargetId`, and `SiteId` survive account and site erasure, and set
    retention: PP18.9 and PP19 add high-volume events.
  - The trigger fires on statements that match no rows, so blanket erasure jobs and database-reset tools must leave
    this table out. Changing the trigger is a migration of its own.
- **PP1.12:**
  - Place the sign-out revocation list.
  - Note that `docs/architecture-decisions/pantrypress.md` has no PP1.10 row.
- **Minor, recorded rather than fixed:** every save now runs change detection twice, once for the guard and once in
  EF Core. That is cheap today; revisit it when the Worker builds page data with many tracked entities.
- **Local note:** a running `aspire run` may apply the `AuditLog` migration to the persistent local database as soon as
  it restarts its migration service. It is additive; nothing to undo.

Verification:

- `dotnet build PantryPress.slnx` gave 0 warnings and 0 errors, built to a separate artifacts path because a running
  `aspire run` held the default outputs; tests ran with the same path as `UseArtifactsOutput` and `ArtifactsPath`.
- `dotnet format PantryPress.slnx --verify-no-changes` is clean.
- `dotnet test PantryPress.slnx`: 704 of 704 passed. The first full run had three failures, all model inventory tests that must
  declare each new table on purpose (the entity list, the tables allowed a `SiteId`, and the migration list); they now
  name the audit log.
- New tests:
  - Unit: action, target, reason, and actor rules, including the publishing-key prefix and the full-width at signs;
    staging with the application clock and the trace id; refusals that stage nothing; two sites keeping their own site;
    the save guard refusing an edited or removed entry; and the reconcile rule.
  - Against the migrated schema:
    - an entry committing with its save, and lost with a failed save, a rolled-back transaction, or a lost concurrency
      race;
    - append order;
    - the context refusing a change or removal, and the trigger refusing raw SQL, `ExecuteUpdate`, and `ExecuteDelete`;
    - the trigger present and enabled.
  - Through the real ApiService routes:
    - each account event writing exactly one entry, and refused or repeated requests writing none;
    - lockout from a password change, from sign-in, and from wrong two-step codes, each audited once;
    - an authenticator code at sign-in not audited;
    - two accounts' trails kept apart;
    - no column holding the account's email.
  - Platform: grants, revocations, and the operator's list recorded with their actor and reason; a refused reason
    changing nothing.
  - The model inventory tests now declare the table.
- Reviewers: `architecture-reviewer` (with the Template K review) and `site-isolation-auditor`. Neither found a blocker.
  Their two major findings - the destructive rollback, and the tenancy exception that was not written down - are fixed
  above. Of the minor ones, the boundary scan's reach, validation before staging, the at-sign normalization, the
  publishing-key target, and the missing tests are fixed; the rest are recorded above.

### PP1.12 Identity and boundary audit - done

```text
SCOPE: Audit PP1 against ACCT-001–005 and PSEC-003. Report only.
CONSTRAINT: Template L.
UTILIZATION: architecture-reviewer, api-contract-checker.
BEHAVIOR: Return findings and checkpoint evidence. Wait for approval before fixes.
```

*PP1.12 delivery note (done 2026-10-24):* the audit (Template L, `architecture-reviewer` and `api-contract-checker`)
found 1 blocker, 4 high, 8 medium and 13 low findings; the owner approved "fix all and run until green". Everything below
shipped in one change, which the constitution would have split - three seams at least (PP1.10b, session revocation, the
small rule fixes) - and is recorded here as one because the owner asked for one.

- **Blocker, fixed: PlatformAdmin went to whoever signed up first with a listed address.** A grant now needs a confirmed
  address; an unconfirmed listed address is a masked warning and resolves to nobody, which also revokes a holder in that
  state. Two-step sign-in needs a confirmed address too, and a reset that first confirms one clears a second factor the
  account already had, so a squatter's authenticator cannot stand between the owner and the account.
- **High, fixed:**
  - *Anonymous routes shared one rate-limit bucket.* The gateway vouched only for signed-in requests, so ApiService saw
    its address for sign-up and reset. It now sends its secret on every forwarded admin request; a test replays what the
    real gateway forwards into the real ApiService.
  - *Sign-out revoked nothing, and sessions slid for ever.* Each complete session has an id; sign-out revokes it
    (`RevokedSessions`, migration `SessionRevocations`), so a copied cookie stops working, and a session ends 12 hours
    after sign-in however busy (decision log). Proven end to end over real TLS.
  - *PP1.10 was unfinished.* PP1.10b, above.
  - *ACCT-004 had no prompt.* PP19.11a now builds account deletion and export; the requirement maps to PP19.
- **Medium, fixed:** the second-step claim counts only while the account has two-step sign-in; `GET /session` asks the
  API (and an address confirmed since sign-in now shows as confirmed); the gateway's session routes declare every status
  they answer, and an unreachable or slow API is a 502; the admin document declares the gateway's own answers, with each
  route's 401 worded for whether it needs a sign-in; a lockout's 429 carries `Retry-After`; a right authenticator code
  sent twice is refused as used without counting toward lockout; writes that cannot be repeated say in the document what
  a retry after a lost answer gets; the module-boundary scan sees a root-namespace or relative reference; and the
  hand-offs that lived only in delivery notes are in their prompts (PP5.13 and PP5.14 cite ACCT-001's publishing check,
  PP12.3 puts the spam challenge on sign-up, PP2.8 reserves `app`).
- **Low, fixed:** the session-refresh filter reads the action's result, not a response status that is still 200; rules
  moved from the facades into `Business/AccountSecurityRules.cs`; the principal is named by the account id; the
  internal surface answers strangers before authentication and the rate limiter (and declares `credentials`' 409);
  `ValidationProblemDetails` has `errors` optional; a catalogue test checks every problem code; the expired half-finished
  cookie is cleared; the gateway's mirrored records sit in `Contracts/`; Domain references no `Microsoft.Extensions.Hosting`;
  PP1.1 is marked done with a note, and the decision log has PP1.10's row.
- **The reviews of the fix changed it again** (`architecture-reviewer`, `api-contract-checker`, `integration-reviewer`):
  the session checks have their own limits on both sides, never the sign-in ceiling; turning two-step sign-in on keeps the
  caller's session, with the second step passed (`X-PP-Second-Step`); everything the proxy forwards is marked
  (`X-PP-Via`) and the internal surface refuses it; sign-up is one save; the Worker sends email only with a provider;
  stale links are not sent; the gateway's key ring is apart from the one that signs reset links; and the admin v1
  "pre-release" rule is written down, in `api-contract.md` and the decision log.
- **Declined, with reasons:**
  - *Rotating the stamp when an address is first confirmed* (architecture review). It would end a squatter's running
    sessions, but a squatter who knows the password signs straight back in, and it would break confirming twice with the
    same link. The residual risk - an owner who confirms an account they never created - is PP19's: the operator console
    should email the account when PlatformAdmin is granted, and operators should sign up themselves before they are listed.
  - *Equalizing the timing of a wrong password on a real account with an unknown address* (contract review). The real
    one saves a failed-attempt count; faking a write for an unknown address would be a write for nothing. The gateway's
    20-a-minute limit per address bounds the measurement; PP12.3 reviews sign-up and sign-in timing together.
  - *A second secret for the gateway's session client* (architecture review). The proxy mark gives the same protection -
    nothing forwarded reaches the internal surface - without another secret to provision and rotate.
- **Deployment ordering:** ship the gateway and ApiService together. A new ApiService needs `X-PP-Sid`, and a new gateway
  needs `sessions/current` and `sessions/end`; everyone is signed out once, because older cookies carry no session id.
  The migration job runs first, as always. `SessionRevocations` (Template K, reviewed with the change by
  `architecture-reviewer`) is additive: one table, a binary-collated primary key, an index on `ExpiresAt`, no foreign keys
  and no backfill. Rolling it back drops the sign-outs, which older code never reads.
- **Checkpoint (PP1.12):** "A blogger can register, verify email, sign in through the BFF, refresh, and sign out without
  exposing a token" - met, and proven by `AccountEmailEndToEndTests` on the real AppHost over TLS: sign-up through the
  gateway, the Worker's email in the mail catcher, the link confirming the address, sign-in setting only `HttpOnly`,
  `Secure`, host-only cookies with a view that holds no stamp or token, the session read again from the database's word,
  and sign-out after which a copy of the cookie reaches nothing. There is no admin screen for it yet (PP3.8).
- **Not verified:** no browser has run the flow (PP3.8 builds the screens); the Terraform was edited without a
  `terraform validate` (no CLI here); the deployed key-ring behaviour waits for PP19.
- **Environment, not code:** one full run had 20 account tests answer 500 because their fixture's SQL container stopped
  answering mid-run (connections forcibly closed, then "server not found"), the load symptom PP1.10a recorded. The same
  tests passed alone, and the final full runs passed twice in a row.
- **Local note:** an `aspire run` that restarts its migration service applies the additive `SessionRevocations`
  migration to the persistent local database. Nothing to undo. Signed-in browsers are signed out once.

Verification: `dotnet build PantryPress.slnx` (0 warnings, built to a separate artifacts path because a running
`aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test
PantryPress.slnx` 809 of 809, twice in a row (704 before this prompt).

## PP2 — Sites, membership, and tenancy

### PP2.1 Sites module skeleton - done

```text
SCOPE: Create Modules/Sites/ in PantryPress.Domain with Facade, Business, Data, and Managers folders and the
module registration, plus ModuleBoundaryTests for the domain.
CONSTRAINT: PP-D01; backend.md.
RESTRICTION: No entities yet.
BEHAVIOR: Plan, wait for approval, implement, run the boundary tests.
```

*PP2.1 delivery note (done):*

- **What differs from the prompt.** The prompt named the folders Facade, Business, Data, and Managers. With the owner's
  approval the module uses the names Accounts and Platform already use, which are the seam's own (`Contracts/`,
  `Facades/`, `Business/`, `DataLayer/`, `Repositories/`, `Gateways/`, `Validation/`), and has no `Managers/`: that
  name is the shared kernel at the domain root. Git keeps no empty folder, so the skeleton is the declared folder list
  in `ModuleBoundaryTests`; each folder appears with its first file (PP2.2 on). Recorded in the decision log;
  `backend.md` and the `add-site-entity` skill (which said `Modules/<Context>/Domain/`) updated to match.
- **Registration.** `Modules/Sites/SitesServiceCollectionExtensions.AddSites()` registers nothing yet; ApiService calls
  it, because admin site routes live there. Other hosts call it when they need something from it.
- **Boundary tests.** The module tests moved from `DomainBoundaryTests` (which keeps the assembly-reference and
  unfiltered-table tests) to `Modules/ModuleBoundaryTests`, beside a shared `DomainSource` locator, and gained the
  rules inside a module: declared contexts only, a registration at each root, only the seam's folders, one namespace
  per file and it is the folder's, layers naming only what the seam allows, nothing below a facade naming a facade
  interface, EF Core only in repositories and entity configuration, no query leaving the data layer, Business without
  I/O, Contracts without root or Identity types, and no module naming another's root types. Scans ignore comments and
  join qualified names broken across lines; global usings and aliases into a module are refused. Each rule was checked
  by planting a violation and seeing it fail, then removing it.
- **Reviewer:** `architecture-reviewer` found no blocker. Its findings changed the tests and docs as above: the stale
  skill, facade interfaces reachable through Contracts, EF Core outside repositories, the ways to hide a reference, root
  types in contracts and composition roots, the Business I/O list, unchecked root files, and the wording of
  `backend.md`. Recorded, not changed: Accounts' facades hold the unit of work, and its email handler calls
  `IEmailGateway` itself; both are existing PP1 code the test allows.
- **Not verified:** nothing; there is no runtime behaviour beyond the empty registration.

Verification: `dotnet build PantryPress.slnx` (0 warnings, built to a separate artifacts path because a running
`aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test
PantryPress.slnx` 836 of 836 (809 before this prompt).

### PP2.2 Site entity - done

```text
SCOPE: Add Site per SITE-001 with a platform-wide unique slug index and a concurrency token.
CONSTRAINT: SITE-001; Template C.
RESTRICTION: Site is the tenant root, so it carries no SiteId of its own; document that exception.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

*PP2.2 delivery note (done):*

- **What shipped.** `Modules/Sites/Site` with `SiteId`, `SiteStatus`, and `SiteLimits` in `Contracts/`: name (60), tagline
  (120, empty hides it), slug, status, IANA time zone, BCP 47 locale, creation time, and a `rowversion` concurrency token.
  The slug is unique platform-wide (`UX_Sites_Slug`) under a binary collation, and `CK_Sites_Slug_Shape` admits only 3 to 40
  of `a-z`, `0-9`, and `-`, so the byte-for-byte index is as strict as a case-insensitive one. That check is a backstop;
  the slug policy is still PP2.8's. Status is stored as fixed numbers, and `CK_Sites_Status` is built from the enum.
  Migration `Sites`: one new table and index, additive; its rollback refuses while any site exists.
- **What differs from the prompt.** SITE-001 also lists design settings, the current theme snapshot, and settings. With
  the owner's approval they land with PP11.1, PP11.4, and PP8 and PP10, which already own them. No repository yet: its
  first caller is PP2.10 (owner-approved; Template C's skill lists one).
- **The exception, documented.** `Site` is the tenant root: no `SiteId`, not `ISiteOwned`, no filter. This is recorded in
  `tenancy.md` ("Tenant root"), on the entity, and in the decision log. Because nothing filters it, `tenancy.md` now also
  says that only the Sites module reaches the table (`DomainBoundaryTests` scans every host; checked by planting three
  violations), that a read by a slug the caller holds no claim to answers existence only, that PP2.5's filter and stamp key
  on `ISiteOwned` and never on the `SiteId` type, that the resolution caches are keyed by slug or host and invalidated when
  one changes hands, and that slugs and hosts are checked for ASCII in .NET before a lookup.
- **Tests.** Model tests pin the missing `SiteId` and filter, the unique binary slug index, the row version, the limits,
  and the status numbers. Integration tests against SQL Server cover two look-alike sites kept apart on update, a held
  slug refused to a second site, a stale save refused, every off-shape slug refused (upper-case, short, space, underscore,
  dot, accented, Cyrillic look-alike, trailing space, empty), an over-long slug refused rather than cut short, an
  out-of-range status refused, and the shortest and longest slugs accepted.
- **Reviewers.** `site-isolation-auditor` and `architecture-reviewer` found no blocker. The architecture reviewer's major
  finding (the trailing-space slug might pass the check) was closed by the existing test, which ran green against SQL
  Server. Changed after review: the tenant-root rules and boundary scan above, the status check built from the enum, the
  rollback guard, and two tests that claimed more than they proved. Left for later: the two-site checks a route owes
  (another site's member cannot read A by slug; listing returns only the caller's sites; availability hides the holder)
  belong to PP2.7 and PP2.10; `SiteHost`'s host column should take the same binary collation (PP2.9). Test files that
  name `Site` put the module's `using` inside their namespace, because `PantryPress.Tests.Site` would otherwise win.
- **Not verified:** the local database. The migration was generated while `aspire run` was up, which applied it
  as soon as it existed in earlier prompts. After that, its status check was edited in place from `BETWEEN 0 AND 5` to
  `IN (0, 1, 2, 3, 4, 5)`, the same meaning, and its rollback gained the guard. A local database that already took the
  first version keeps the old text, which allows the same values.

Verification: `dotnet build PantryPress.slnx` (0 warnings, built to a separate artifacts path because a running
`aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test
PantryPress.slnx` 858 of 858 (836 before this prompt), with the model matching the migrations.

### PP2.3 Site membership and roles - done

```text
SCOPE: Add SiteMember (SiteId, UserId, role Owner | Editor | Author, status) and membership policies.
CONSTRAINT: SITE-010, PP-D03; tenancy.md and auth.md.
RESTRICTION: Every site has at least one Owner; the last Owner cannot leave or be demoted.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

*PP2.3 delivery note (done):*

- **What shipped.** `Modules/Sites/SiteMember`: SiteId, AccountId (the prompt's UserId, named as Accounts names it), role
  Owner | Editor | Author, status Active | Suspended, creation time, and a `rowversion`. The primary key is (SiteId,
  AccountId), with a `Restrict` foreign key to `Sites`, an account index, and role and status checks built from the enums.
  Migration `SiteMembers` adds the table and the trigger `TR_SiteMembers_KeepAnOwner`; its rollback refuses while any
  member exists. A read path `ISiteMembershipFacade` -> `SiteMembershipDataLayer` -> `SiteRepository` and
  `SiteMemberRepository`, `SiteRoles` (what each role covers, and the last-owner rule), and `SiteSlugs` (a slug's shape and
  ASCII, checked before any lookup). In ApiService, the `SiteOwner`, `SiteEditor`, and `SiteAuthor` policies, a result
  handler for their 404 and 403, `SitePolicyConvention`, and the admin document's declaration of both answers.
- **Owner-approved choices.** Membership is part of the tenant root: a required `SiteId`, but not `ISiteOwned` and no
  filter, because it is read before any site is known (`tenancy.md`, "Tenant root"). The owner rule is a business rule plus
  a database trigger. The policies answer 404 `site-not-found` and 403 `site-role-required` now, rather than at PP2.4. Two
  statuses, and removal deletes the row.
- **What differs from the prompt.** "The last Owner cannot leave or be demoted" holds in the database from today, and
  `SiteRoles.CanChangeRole`, `CanSuspend`, and `CanRemove` hold it as a plain refusal (`last-owner`). No route changes a
  membership yet, so those rules' first caller is PP2.11, and the first Owner is written by PP2.10's create-site facade.
- **Also changed: committed-snapshot reads.** Azure SQL reads committed snapshots by default; containers don't. The
  migration service now turns them on where they are off, before migrating, so local, test, and Azure databases read alike.
  Your local database switches the next time the migration service runs; that run rolls back any open session, such as
  the data explorer's.
- **Reviewers.** `site-isolation-auditor` and `architecture-reviewer` found no blocker. Their two majors were fixed:
  - the API host decided the role rule itself; the decision is now `ISiteMembershipFacade.AuthorizeAsync`, and a new test
    keeps every host to a module's contracts and registration;
  - nothing tied `{siteSlug}` routes to site policies; `SitePolicyConvention` now refuses to start otherwise.

  Minors fixed:
  - the membership cache is keyed by account and normalized slug, inside the request-scoped facade;
  - the trigger checks only rows that were active owners, and its comment states the whole-transaction rollback and the
    deadlock case;
  - the committed-snapshot switch moved from the test fixture to the migration service;
  - the tenant-root boundary scan now covers `DbSet<>`, `typeof`, bare `SiteMembers`, and SQL after a keyword;
  - auth.md now records the site policies, the 404 and 403, Suspended, and the per-request cache;
  - `tenancy.md` lists the trigger's limits.

  Recorded, not changed: authorization runs before the rate limiter, so a signed-in caller's site lookups are not limited
  yet. The answers reveal nothing, and the order is revisited with PP2.10's first real route.
- **Tests.**
  - Model: key, index, foreign key, trigger, checks, and the enum numbers the trigger's SQL relies on.
  - Business: the role-coverage table and the last-owner rule; the slug shape, including look-alike letters.
  - Integration (SQL Server):
    - the last owner kept against update, suspension, delete, and a bulk delete;
    - a bulk update across two sites refused whole;
    - an owner moved to another site refused;
    - a staggered race and ten simultaneous ones, each leaving exactly one owner;
    - a deleted site exempt, duplicates and orphans refused, committed snapshots on;
    - one lookup per account and slug per request.
  - Two-site lookups through the facade, and over HTTP against the real lookup: one account with its own role on each
    look-alike site; a stranger's answer for a real site byte-for-byte that of no site, headers included; suspended
    members and deleted sites reach nothing; a slug in any case.
  - Policies: the full role matrix, a 401 without a lookup, every startup refusal, and the document's 404 and 403.
  - Checks proven by planting: the race test fails without the lock hint; the boundary scans catch six planted
    references.
- **Left for later.** PP2.10: write a site and its first Owner in one transaction, and test it. PP2.11: answer 51003 and
  1205 on member changes. PP2.4: build the site context on this lookup, not a second one. Seeing the new answers in the
  admin client waits for PP3.

Verification: `dotnet build PantryPress.slnx` (0 warnings, built to a separate artifacts path because a running
`aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test
PantryPress.slnx` 937 of 937 (858 before this prompt), with the model matching the migrations.

### PP2.4 Site-owned contract and request context - done

```text
SCOPE: Add the ISiteOwned contract, a site request context, and route-based site resolution for admin routes
from the authenticated membership.
CONSTRAINT: PP-D03, ARCH-015; tenancy.md.
RESTRICTION: Never accept SiteId from a body, query string, or client header. Unknown and inaccessible sites
are indistinguishable (404).
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*PP2.4 delivery note (done):*

- **What shipped.** `Managers/Tenancy`: `ISiteOwned`, `ISiteContext`, the internal `ISiteContextWriter`, the scoped
  `SiteContext` (set once), `SiteScope`, and `AddSiteContext`. `SiteId` moved here from `Modules/Sites/Contracts` (no schema
  change). `ISiteMembershipFacade.AuthorizeAsync` enters the site when it admits the caller, with an overload taking every
  least role a route stacks, and the API's site handler asks once for all of them. `SiteIdBindingConvention` refuses to
  start a host whose routes could bind a site id. Nothing implements `ISiteOwned` yet; the filter and stamp are PP2.5's.
- **Owner-approved choices.** `SiteId` in the kernel; the outbox and audit writers take `SiteScope.Current` or
  `SiteScope.None` instead of `Guid? siteId`, as `tenancy.md` assigned to this prompt.
- **What differs from the prompt.** The prompt's restriction names a body, query string, and header; the convention also
  covers route values, form fields, binder-renamed properties, plurals, and polymorphic subtypes. "Unknown and inaccessible
  are indistinguishable" already held from PP2.3; this prompt adds that a refusal enters no site and that a stranger is
  refused before the body is read.
- **Reviewers.** `site-isolation-auditor` and `architecture-reviewer` found no blocker. Both raised one major, now fixed:
  the convention skipped interface-typed parameters (`IReadOnlyList<T>`, `IEnumerable<Guid> siteId`), missed plurals, and
  missed binder-renamed nested properties. Minors fixed:
  - stacked site policies decided per requirement, so a 403 could still leave the site entered; now one decision;
  - the boundary scan missed a fully qualified `SiteContext`, and the writer is now internal as well;
  - an outbox message with a `SiteId`-typed property under another name was allowed; the refusal's wording was stale;
  - `OutboxDelivery.SiteId` and `OutboxEnvelope.SiteId` were raw `Guid?`;
  - the docs overstated "a refusal leaves none" for non-site policies, and did not say that the Worker has no resolver yet.

  Recorded, not changed:
  - an untyped body (`JsonElement`, `object`) and direct reads of `Request.Query`, `Headers`, `Cookies`, or `Form` are
    not checked; no controller does either today;
  - the Worker enters no site context until the first site message type adds the dispatcher's resolver.
- **Tests.**
  - Unit: the context's set-once rules, scopes, and repeated registration; `SiteScope` for the outbox and audit (no site,
    site action without a site, account work inside a site request, an unknown scope).
  - Convention: fifteen planted routes, each refused at startup, and one that only reads the context, which starts.
  - Integration (SQL Server), two look-alike sites:
    - one account reaches each site as that site;
    - a site id in the query, headers, or body changes nothing;
    - a stranger's malformed body gets the same 404 as no site, while the owner gets 400;
    - an owner-only route stacked on an author policy admits A's owner and refuses B's author;
    - through the facade: a decision enters the site, a refusal or a partly met role enters none, `FindAsync` enters
      nothing, and a second site throws.
  - Boundary: only the resolvers name the writer, with planted references the scan must catch.
- **Left for later.** PP2.5: the filter and stamp read `ISiteContext`. PP2.12: host resolution joins the resolvers. The
  first site outbox message: the dispatcher enters the context from `OutboxDelivery.SiteId`.

Verification: `dotnet build PantryPress.slnx` (0 warnings, built to a separate artifacts path because a running
`aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test
PantryPress.slnx` 980 of 980 (937 before this prompt).

### PP2.5 Global site query filter and SiteId stamping - done

```text
SCOPE: Add the global query filter for ISiteOwned entities and a SaveChanges interceptor that stamps SiteId
from the request context and forbids changing it.
CONSTRAINT: ARCH-015; tenancy.md.
RESTRICTION: IgnoreQueryFilters is prohibited outside documented operator and erasure paths.
UTILIZATION: site-isolation-auditor.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*PP2.5 delivery note (done):*

- **What shipped.** In `Managers/Tenancy`:
  - `SiteQueryFilterConvention`, a model-finalizing convention. It puts the EF Core 10 named filter `site` on every
    `ISiteOwned` root, makes `SiteId` required and a concurrency token, and refuses a model that would escape it.
  - `SiteStampInterceptor` stamps a new row and refuses any other site-owned write that is not wholly the context's site.
  - `SiteIdUpdateGuard` refuses a bulk update that sets `SiteId`.
  - `SiteIdConverter`, `ISiteScopedDbContext`, and `ResolvedSiteContext`.

  `PantryPressDbContext` takes `ISiteContext` and refuses pooled options or any site context but the kernel's own.
  `AddPantryPressDbContext` registers it per scope. A site-owned query with no site throws.
- **Owner-approved choices.** One context per scope, never pooled: ApiService, Worker, and MigrationService moved from
  Aspire's `AddSqlServerDbContext` to `AddPantryPressDbContext` plus `EnrichSqlServerDbContext`. Throw, not empty, with
  no site.
- **What differs from the prompt.**
  - Erasure is not a separate exception. The PP0.2 reconciliation stands, and erasure is `Modules/Platform/` code.
  - The plan said EF Core refuses to pool this context. It does not: it pooled it with a root-provider site context
    that every request would share. The context now refuses pooled options itself.
  - From the review, the model also refuses:
    - a reference between site-owned types not keyed by `(SiteId, id)`;
    - a non-site-owned dependent of a site-owned type, implicit join tables included;
    - TPT, TPC, and entity splitting;
    - turning the `SiteId` token off.
  - The migration service's wrapper is now `AddMigrationDbContext`, and the two per-property `SiteId` conversions in
    Sites gave way to the global converter, with no model change.
- **Reviewers.** `site-isolation-auditor` and `architecture-reviewer` found no blocker. Majors, fixed:
  - one site could attach a row under another site's row by id, and a delete would then cascade across;
  - a non-site-owned dependent or join table went unfiltered;
  - the context accepted any `ISiteContext`.

  Minors fixed:
  - a projected bulk update slipped the guard;
  - the `IgnoreQueryFilters` scan missed type arguments, a next-line parenthesis, and method groups, and counted any
    comment as a reason (it now wants `// Cross-site:`);
  - convention results were unchecked;
  - `AddDbContextFactory` was not scanned;
  - there was no health-check test;
  - the skill's token wording, the duplicate converters, the shared method name, and comment nits.

  Recorded, not changed:
  - `Database.SqlQuery` and `ExecuteSql` are not filtered and no test confines them to repositories (backend.md's rule,
    not this prompt's);
  - the `AutoDetectChangesEnabled = false` path is untested;
  - no boundary test refuses an `ISiteContext` implementation outside the kernel; the context's own check makes one
    harmless to it.
- **Tests.**
  - Integration (SQL Server), two look-alike sites over probe types planted through a replacement model customizer, in
    a `probe` schema created and dropped by the test (only the migration service builds the product schema):
    - reads, counts, `Find`, `Include`, and `FromSql` stay inside the site;
    - bulk updates and deletes reach only the site's own rows;
    - two scopes share one cached model and each reads its own site;
    - a site entered after the context was made is the one it filters by;
    - no site throws;
    - stamping;
    - a changed site, an attached foreign row, a stub delete, and a lie caught by the concurrency token;
    - references across sites by add, stub, re-parenting (synchronous save), and raw insert;
    - a cascade stays inside its site.
  - Model: every guard on the probe and product models; each refusal; a one-table hierarchy accepted; pooling and a
    stand-in site context refused; ApiService and MigrationService per scope with the resolver's context, retries, and
    ApiService's health check; no host pools or uses a factory.
  - Boundary: the `IgnoreQueryFilters` scan with nine planted cases.
- **Left for later.** The first real site-owned entity (`add-site-entity`, now with the reference rule). PP2.6 cache keys.
  PP2.7 the two-site harness. The Worker's site resolver.

Verification: `dotnet build PantryPress.slnx` (0 warnings, built to a separate artifacts path because a running
`aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test
PantryPress.slnx`: 1015 of 1016 (980 before this prompt). The one failure,
`AdminAppTests.Gateway_serves_the_admin_app_and_keeps_its_own_health_endpoint`, timed out waiting for the Angular dev
server under full-suite load; it passed in an earlier full run and on its own (4 of 4), and this prompt does not touch
it.

### PP2.6 Site cache-key helper - done

```text
SCOPE: Add a cache-key helper so every site-scoped cache key begins with the site identifier.
CONSTRAINT: ARCH-015, PSEC-007; tenancy.md.
BEHAVIOR: Plan, wait for approval, implement with collision tests.
```

*PP2.6 delivery note (done):*

- **What shipped.** `SiteCacheKey` in `Managers/Tenancy`: `{siteId}:{area}:{part}:…`, beginning with the site's lowercase
  id.
  - It is built only from the `ISiteContext` a resolver entered.
  - The area has a closed shape.
  - Each part is non-empty, well-formed text, with `%` and `:` escaped.
  - A key is at most 1,024 UTF-8 bytes.
  - `Prefix` covers a site, or one area of it.

  `PlatformCacheKey` (`platform:{area}:…`) covers what belongs to no site. A boundary test refuses any raw cache API
  outside a declared adapter (none yet).
- **Owner-approved choices.** The site comes from the context, never a passed `SiteId`. No Redis hash-tag braces. A
  length cap, with long values hashed by the caller.
- **What differs from the plan.**
  - `PlatformCacheKey` was added, from the audit. A slug may legally be spelled like a site id, so "platform keys start
    with a word" needed a type behind it.
  - The cap counts UTF-8 bytes, not characters.
  - `Prefix` also takes an area.
- **Reviewer.** `site-isolation-auditor`.
  - **Blocker, fixed:** `For` and `Prefix` accepted any `ISiteContext`, so a stand-in could name another site. Both now
    go through `ResolvedSiteContext`.
  - **Majors, fixed:**
    - the platform-key promise had nothing behind it (`PlatformCacheKey`);
    - the raw-cache scan missed concrete types, namespaces, other Redis surfaces, output caching, keyed and `Add…`
      registrations, packages, and project aliases.
  - **Minors, fixed:**
    - lone surrogates could share a key once encoded as UTF-8;
    - the cap counted characters;
    - an area prefix was missing;
    - personal values are now hashed at any length (rule text);
    - `tenant-provisioning.md` said every Redis key is a `SiteCacheKey`;
    - the look-alike test ids now differ only in their last character;
    - the allow-list now matches exact files.
  - **Recorded, not changed:**
    - a hand-rolled in-process cache (a `ConcurrentDictionary`) and the Node SSR server are not scanned;
    - `Prefix` returns a string, and Redis glob characters in parts are not escaped. Both are for the PP7.12 adapter to
      settle, with routine purges by generation or tag rather than a scan;
    - whether PP2.8's reserved list should refuse id-shaped slugs, which `PlatformCacheKey` makes harmless here.
- **Tests.**
  - Unit:
    - the key's shape;
    - look-alike sites never sharing a key or a prefix;
    - fifteen crafted near-collisions;
    - a part spelling another site's prefix;
    - 10,000 seeded random inputs from a hostile alphabet, all distinct and each decoding back to its input through an
      independent decoder;
    - no site, a stand-in site, and the empty id refused;
    - area prefixes;
    - platform keys never inside a site's key space, even keyed by an id-shaped slug;
    - area, part, byte-length, and malformed-text refusals;
    - ordinal comparison.
  - Boundary: the raw-cache scan over source, projects, and shared props, with twenty-one source and five project
    planted cases.
- **Left for later.** The cache adapter (PP7.12), resolution-cache keys (PP2.12), blob prefixes (PP7.11), purge tags
  (PP9.5).

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default
outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1065 of 1065
(1016 before this prompt). One earlier full run failed two `IdentityModelTests` in the context's constructor. That run's
output was filtered too tightly to keep the message, and five later runs (two full, three unit-only) all passed. The
cause is unknown and recorded here so a recurrence is recognized.

### PP2.7 Two-site test harness - done

```text
SCOPE: Add a reusable test harness that seeds two sites with similar data and asserts no read, write,
cache, or enumeration crosses between them.
CONSTRAINT: ARCH-015, PSEC-007.
BEHAVIOR: Plan, wait for approval, implement and apply it to Site and SiteMember.
```

*PP2.7 delivery note (done):*

- **What shipped.** In `PantryPress.Tests/TwoSite/`:
  - `TwoSiteHarness`, over the shared migrated database, with ApiService on request. It seeds two look-alike sites per
    test with fresh ids:
    - the same name and tagline;
    - slugs one letter apart, A's a prefix of B's;
    - an Owner, Editor, Author, and suspended member each;
    - accounts that cross between the sites in role (Author on A, Owner on B) and in standing (suspended on A, Editor
      on B), and a stranger.
  - `TwoSiteChecks`:
    - `AssertSiteOwnedIsolatedAsync<T>`, the floor for every site-owned entity;
    - `AssertReadPathIsolatedAsync<T>`;
    - `AssertTenantRootUnchangedAsync`;
    - `AssertIndistinguishableAsync`.
  - `TenantRootTwoSiteTests`, which applies them to `Site` and `SiteMember`.

  No production code changed.
- **What differs from the plan.**
  - **No cache check.** The planned `AssertCacheKeysApart` was a key-shape check that could not fail for a cache that
    mixes sites, so it was dropped. The cache the tenant root has today, the facade's per-request lookup, is tested
    directly. The PP7.12 adapter and the PP2.12 resolution caches each owe a behavioral check, named in `tenancy.md`.
  - **Two checks added, from the review:** the read-path check and the tenant-root snapshot check.
  - **The seeded world grew:** an account that crosses status between the sites, and B's slug is now A's plus a letter
    rather than a last letter apart.
  - **The four existing seeding helpers are left as they were**, as planned.
- **Reviewers.** `site-isolation-auditor` and `test-gap-analyzer` found no blocker.
  - **Majors and highs, fixed:**
    - the cache check could not fail (dropped, as above);
    - the generic check never tried B's row passed off as A's, the one write only the `SiteId` token stops;
    - the checks reached only `db.Set<T>()`, so the docs now call the entity check a floor and name what each change's
      own paths owe, including PP2.10 and PP2.11 for the tenant root;
    - no seeded account crossed status between the sites.
  - **Mediums and minors, fixed:**
    - refusals now expect their own exception and message, and the write-back changes a value so a leak would show;
    - the indistinguishability check now pins status and code;
    - a membership suspended between two HTTP requests is refused on the next;
    - the standings table is symmetric;
    - a one-statement write across both sites for one account is refused whole, and neither site's rows change;
    - uppercase slugs over HTTP;
    - the no-site read and a new row naming B joined the generic check;
    - the seed prefix is validated;
    - `ChangeAsync` takes a site;
    - the probe proof runs through the harness.
  - **Recorded, not changed:**
    - A stranger's lookup of a real site makes two queries where an unknown slug makes one, so response time can still
      tell them apart. That is for the tenancy audit (PP2.14).
    - The negative controls show each check failing on a visible leak. They cannot show the generic check failing with
      the stamp or the token removed, because the context will not build without them.
    - A dependent pointing at the other site's principal is proven for the probe types (PP2.5), not by the generic
      check.
    - PP1.10's outbox two-site tests (a mismatched-site enqueue, parallel siblings with separate contexts, a platform
      message that sees no site rows) wait for the Worker's site resolver. They now belong to the first site outbox
      message, through this harness.
- **Tests.**
  - The probe types through the generic check: a root, and a dependent keyed by site.
  - Each check shown able to fail: the same site twice; a `SqlQuery` read past the filter.
  - Tenant root:
    - every account's standing on each site, by the facade, in both cases;
    - roles never carried between sites, and a refusal enters nothing;
    - the request cache keyed by account and slug, and gone after the request (facade and HTTP);
    - one site's owners never counting toward the other's, with the other's rows unchanged;
    - one account's cross-site bulk delete and suspend refused whole;
    - six ways of not reaching a site, each indistinguishable from no site in both cases;
    - a deleted site the same;
    - a too-low role the only 403.
- **Left for later.** Host-header tricks (PP2.12). The cache check (PP7.12). The resolution-cache check (PP2.12). The
  tenant-root route tests (PP2.10, PP2.11).

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default
outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1075 of 1075
(1065 before this prompt).

### PP2.8 Reserved slugs and slug policy - done

```text
SCOPE: Implement the SITE-002 slug policy with a versioned reserved list stored as platform reference data.
CONSTRAINT: SITE-002; PSEC-003; Template A.
RESTRICTION: ASCII only, case-insensitive comparison. The list reserves every host PantryPress itself answers on,
`app` above all: the BFF's `__Host-` cookies are host-only for app.pantrypress.app, and a tenant site on that host
would share the admin app's origin (PP1.12).
BEHAVIOR: Plan, wait for approval, implement with table-driven tests.
```

*PP2.8 delivery note (done 2026-10-27):*

- **What shipped.**
  - `Modules/Sites/Business/SlugPolicy`, pure code. `Check` returns the stored slug or the refusal. `IsWellFormed` is
    there for PP2.10's validator. The order of checks:
    - ASCII, before anything is lower-cased (`SiteSlugs`), so comparison is case-insensitive and no look-alike letter
      becomes a real one;
    - a host name's label: no hyphen at either end, and none doubled, which rules out `xn--` labels a browser shows in
      Unicode;
    - not a site id's 8-4-4-4-12 hex shape;
    - the reserved list.
  - `Business/ReservedSlugs`, versioned (`Version` and each entry's `Since`). Four reasons:
    - platform hosts: `app`, `api`, `www`, `dev`, `mail`, `status`, and others;
    - infrastructure;
    - impersonation;
    - product pages.

    `demo` and `cook` stay claimable.
  - Platform reference data, `ReservedSlugs` (no `SiteId`), seeded from that list. Migration `ReservedSlugs` adds:
    - `TR_Sites_SlugNotReserved`: refuses a new or changed reserved slug (51005); an unchanged one is let through;
    - `TR_ReservedSlugs_NotHeld`: refuses reserving a slug any site holds, a deleted one included (51006, naming the slug
      for the operator). It is created before the seed, so the first list is checked too;
    - the hyphen rules in `CK_Sites_Slug_Shape`.
  - `SiteErrors.SlugUnavailable` (`slug-unavailable`, 409).
- **Owner-approved choices.**
  - A migration that would reserve a held slug fails; it never grandfathers the site.
  - `--` is refused anywhere.
  - Both triggers are in.
- **What differs from the plan.**
  - A refused shape is `Error.Validation` (`validation-failed`) with a `slug` field rather than a new `slug-invalid` code,
    because every field error in the codebase has that shape.
  - The site-id shape rule answers PP2.6's open question (both reviewers asked for it). It is in .NET only, not the
    check constraint.
  - `mta-sts`, `openpgpkey`, and `platform` joined the list after review.
- **Template K.**
  - The migration is not purely additive: it tightens a check on `Sites` and adds a trigger there. No app path writes
    `Sites` before PP2.10, so no row can fail the tighter check.
  - An app build without the `HasTrigger` declaration would fail its writes to `Sites`.
  - Rollback drops both triggers and the table, and restores the looser check. Only reference data goes.
- **Reviewers.** `architecture-reviewer` and `site-isolation-auditor` found no blocker and no major.
  - **Changed after review:**
    - the site-id shape rule;
    - a boundary test keeping `ReservedSlug` to the Sites module and the migrations;
    - the Terraform check widened to every literal host label;
    - a race test for the lock hint, which fails with the hint removed;
    - `IsReserved` made internal;
    - the 51006 message names the slug;
    - wording that claimed no path could skip the trigger;
    - cases for the Kelvin sign, fullwidth letters, a zero-width joiner, and NUL.
  - **Recorded, not changed:**
    - A bulk load skips triggers and checks unless it asks for them, and an operator can disable a trigger or delete a
      reserved row. The .NET policy still refuses, because it reads the list in code.
    - The auditor's trailing-space concern does not hold: `SiteSchemaTests` shows `'pie '` refused by the shape check.
    - `SlugPolicy.Check` names the `slug` field; PP2.10's validator may own that key instead.
- **Left for later.**
  - PP2.10:
    - map 51005, 51006, and the slug index's 2601 to `slug-unavailable`, never the trigger's text;
    - declare the 409 in the admin document;
    - an indistinguishability test across a reserved slug, a held one, a deleted site's, and a case variant.
  - PP2.9 and DOM-001: refuse a custom domain under the platform's own zones (`app.*` above all), and decide whether a
    slug kept by a rename redirect is unavailable.
  - PP2.12: host-header tricks with `app.<zone>` and `xn--` hosts.
- **Not verified: the local database.** With `aspire run` up, the migration may have reached your local database early.
  It was regenerated under the same id after review (three more seed rows and the 51006 message). A local database that
  took the first version lacks those rows and has the older trigger text; drop and re-create it, or roll the migration
  back and forward.

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default
outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1174 of 1174
(1075 before this prompt).

### PP2.9 SiteHost and canonical host - done

```text
SCOPE: Add SiteHost (SiteId, host, kind Subdomain | Custom | WwwVariant, IsCanonical, status) with a unique
host index and exactly one canonical host per site.
CONSTRAINT: SITE-005; Template C.
RESTRICTION: Hosts stored lower-case without port or trailing dot.
BEHAVIOR: Plan, wait for approval, migrate, test.
```

*PP2.9 delivery note (done 2026-10-27):*

- **What shipped.**
  - `Modules/Sites/SiteHost`, with `SiteHostId`, `SiteHostKind` (Subdomain, Custom, WwwVariant), and `SiteHostStatus`
    (Active, Pending) in `Contracts/`.
  - Each host row holds: SiteId, host, kind, IsCanonical, status, creation time, and a `rowversion`.
  - `Business/SiteHostNames.TryNormalize` gives the stored form of a host:
    - ASCII is checked before lower-casing;
    - one trailing dot is dropped;
    - no port;
    - two labels or more, each 1 to 63 characters with no hyphen at either end;
    - 253 characters at most;
    - the last label starts with a letter, so no IPv4 literal is accepted in any spelling.
  - Migration `SiteHosts` adds one table:
    - `UX_SiteHosts_Host`: unique and binary;
    - `UX_SiteHosts_SiteId_Canonical`: filtered to canonical hosts;
    - `IX_SiteHosts_SiteId_Kind`;
    - a `Restrict` foreign key to `Sites`;
    - checks on the host's shape, kind, and status, and one that a canonical host is Active and never a `WwwVariant`;
    - the trigger `TR_SiteHosts_Guard`.

    The trigger refuses losing a canonical host on a site that is not deleted (51007). It also refuses changing a host's
    site or spelling, or making a host a subdomain or no longer one (51008). The rollback refuses while any host exists
    (51009).
- **Owner-approved choices.**
  - `SiteHost` is part of the tenant root, not site-owned. It has a required `SiteId` but no filter or stamp, because the
    public tier resolves a site from its Host before any site context exists. Template C's filter and stamp were set aside
    for the same reason as in PP2.2 and PP2.3.
  - The host index is the second written exception to `SiteId`-leading indexes.
  - Statuses: Active and Pending; PP17 adds the rest.
  - A `WwwVariant` is the other spelling of a custom domain and is never canonical.
  - All of this is recorded in `tenancy.md` ("Tenant root"), `sites.md`, and the decision log.
- **What differs from the plan.**
  - After review:
    - the last-label rule became "starts with a letter", because `127.0.0.0x1` passed the all-digits rule;
    - the trigger also refuses a kind change to or from `Subdomain`;
    - a restore from `Deleted` must also find a canonical host.
  - `Kind` can change between `Custom` and `WwwVariant`, because switching between apex and `www` swaps them.
- **What later prompts must do.**
  - Moving the canonical host has to be one statement (`ExecuteUpdate`). Two tracked updates always fail, whichever order
    they reach the database in (tested).
  - Two moves on one site at the same instant can deadlock (1205), and callers must answer that too.
- **Template K.**
  - Additive: one new table and trigger, with no backfill. Nothing outside tests creates a site yet.
  - No code reads the table yet, so it can ship ahead of PP2.10 and PP2.12.
  - Rollback refuses while any host exists.
- **Tests.**
  - Model: keys, indexes, foreign key, trigger, checks, and the enum numbers.
  - `SiteHostNames`: a table of accepted and refused host names, including the Kelvin sign, fullwidth, zero-width joiner,
    dotless ı, Cyrillic, ideographic full stops, ports, and IP literals.
  - Against SQL Server:
    - the same shapes run through the normalizer and the check, so the .NET rule and the schema agree;
    - a held host refused to another site;
    - a second canonical host refused;
    - losing the canonical host refused, whether by update, delete, or a bulk delete; a deleted site is exempt;
    - the one-statement move passes and two statements fail;
    - site, spelling, id, and subdomain kind are fixed;
    - a stale save is refused.
  - Two-site:
    - the harness seeds each look-alike site's canonical subdomain;
    - `AssertTenantRootUnchangedAsync` now covers hosts;
    - B cannot take A's host as a pending or active domain, or in upper case or with a trailing dot;
    - B's host changes leave A alone;
    - a bulk write across both sites' hosts is refused whole.
  - Boundary: the tenant-root scan covers the host set, type, configuration, and table, with planted cases.
- **Reviewers.** `site-isolation-auditor` and `architecture-reviewer` found no blocker.
  - **Fixed:**
    - both flagged the claim that a cached host lookup goes stale only when a row comes or goes. That is true of host to
      site, not of status, kind, or canonical, which change in place, and the wording now says so;
    - the hex IPv4 gap;
    - the kind guard;
    - the restore precondition;
    - the two-site spellings;
    - the missing decision-log row.
  - **Recorded, not changed:**
    - nothing refuses a custom host under the platform's zones (`app.<zone>`, `www.{slug}.<zone>`, a slug nobody holds
      yet). The zone is configuration, and dev runs under `dev.pantrypress.app`, so the database cannot say it; this moves
      from PP2.9 to PP17.2;
    - a Pending claim holds the unique index, so one site could block another's real domain (PP17: expire claims or
      filter the index);
    - nothing enforces that every host query is keyed by host or by a resolved site, the same gap `SiteMember` has.
- **Left for later.**
  - PP2.10:
    - write the site, its first Owner, and its subdomain host in one transaction;
    - map 2601 on the host index, and 51007 and 51008, to a neutral answer such as `slug-unavailable`;
    - availability checks `SiteHosts` too, because a renamed site's old host still holds its address.
  - PP2.12:
    - resolve Active hosts only, through `SiteHostNames` after splitting off the port, and with a real IP-literal check;
    - a Pending host answers exactly like an unknown one;
    - key the cache with `PlatformCacheKey`, and refresh a site's entries on every write to its hosts and on every
      change of its status;
    - add a two-site test that a canonical host moving and a custom domain changing hands never send A's readers to B;
    - the auditor's host-header list, including `www.` plus the other site's host and `127.0.0.0x1`.
  - PP17:
    - platform zones;
    - pending claims;
    - pairing a `WwwVariant` with its custom domain;
    - a site-owned row that refers to a host keys it by `(SiteId, Id)`, which needs an alternate key here.
- **Not verified.**
  - **The local database.** With `aspire run` up, the migration may have reached your local database before its trigger
    and shape check were edited after review. If the local `SiteHosts` table is there, drop and re-create the database,
    or roll the migration back (it is empty) and forward.
  - **A test that fails intermittently, and failed before this prompt too.** These two tests fail in some full runs:
    - `IdentityModelTests.Context_refuses_a_host_that_did_not_pin_the_identity_schema`;
    - `IdentityModelTests.Host_identity_settings_cannot_change_the_pinned_schema`.

    They throw EF Core's `ManyServiceProvidersCreatedWarning` once more than 20 internal providers already exist in the
    process, which depends on test order. They pass alone, and one of two runs failed them on a clean checkout of
    `51d6f76`.

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default
outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1289 of 1291
(1174 before this prompt), with the model matching the migrations. The two failures are the intermittent ones above, and
they pass alone.

### PP2.10 Create, list, and read sites - done

```text
SCOPE: Add endpoints to create a site (with its subdomain SiteHost and the creator as Owner), list the
caller's sites, read one site, and check slug availability.
CONSTRAINT: SITE-002, SITE-003; Template B.
RESTRICTION: Plan limits arrive in PP14 through the entitlements facade. Availability never reveals the
holder of a taken slug.
BEHAVIOR: Plan, wait for approval, implement with two-site tests.
```

*PP2.10 delivery note (done 2026-10-27):*

- **What shipped.**
  - Four admin routes in `SitesController`, which injects only `ISitesFacade`:
    - `POST /api/v1/sites` answers 201 with `Location` and the new site. It is `[Idempotent]`, with the key optional.
    - `GET /api/v1/sites` lists the caller's active memberships on sites that are not deleted, by slug. `limit` runs 1 to
      100 (default 50) and the cursor is opaque.
    - `GET /api/v1/sites/{siteSlug}` sits behind `SiteAuthor`.
    - `GET /api/v1/slug-availability?slug=` answers `{slug, available}`.
  - Across the seam:
    - Contracts: `ISitesFacade`, `SiteRequests` (no site id anywhere), and `SiteAudit` (`site.created`).
    - Business: `SiteSubdomains`, `SiteListCursor`, and `SiteText` (one-line plain text, BCP 47 shape).
    - Validators for the create request, the list query, and the availability query.
    - `SitesFacade` and `SitesDataLayer`.
    - Repositories: `SiteHostRepository`, a list and an existence read on `SiteRepository`, and `SitesUnitOfWork`.
    - `SitesOptions.SubdomainZone`, checked at startup.
  - Wiring:
    - The AppHost sets the zone to `localhost` locally (SITE-009) and to a parameter otherwise. The Terraform sets it to
      `dns_zone_name`.
    - Each site route has its own per-account limit: list and read 60 a minute, address checks 120 a minute, creates 5
      an hour.
  - An account whose email address is not confirmed owns at most one site (`Sites:UnconfirmedAccountSiteLimit`,
    owner-approved after review), counting every Owner membership, deleted sites included. Past it, create answers 403
    `email-not-verified` before the address is asked about. The save counts again inside its transaction under
    `UPDLOCK, HOLDLOCK` on the account's memberships, so simultaneous creates cannot all pass.
- **Owner-approved defaults (DECIDE, stated).**
  - A new site starts `Live`, with no `sites.provision`, because no handler exists (G-01).
  - Creating before the email is confirmed is allowed.
  - No plan limit until PP14.
  - A slug is never reissued: reserved, held by any site (deleted included), or still some site's host all answer as one.
- **What differs from the plan.**
  - Availability moved from `/api/v1/sites/availability` to `/api/v1/slug-availability`, so it can never shadow a site whose
    slug is `availability` (`api-contract.md` updated).
  - Races are settled by re-asking after a failed save rather than reading SQL Server error numbers. This meets PP2.8's and
    PP2.9's request to map 2601 and 51005 to 51008, and no database message reaches a caller.
  - From review:
    - a retried save that meets its own committed site counts as saved;
    - creating sites got its own hourly limit, and a refused create counts toward it;
    - `AddSites` no longer registers the clock (the host does);
    - availability is validated with FluentValidation;
    - a site with no canonical host fails with a named error.
- **Also changed.**
  - A query parameter that fails to bind is reported under its own name (`limit`), not as an unnamed field.
  - The admin document now:
    - gives a 201 its `Location` header;
    - names both codes a 409 on an idempotent create can carry;
    - drops the unreachable 403 from `SiteAuthor` routes.
  - The resolver boundary test names `SiteMembershipFacade.cs` and `SitesFacade.cs` by file instead of the whole folder.
  - `tenancy.md` rules 2 and 4 name site creation as the second place a site is entered.
- **Tests.**
  - Business and validators: zones, cursors, locales, one-line text with unpaired surrogates, and each field's error.
  - Two-site, over ApiService against SQL Server:
    - create writes three rows and one audit entry, and A's and B's tenant-root rows stay unchanged;
    - held, case-variant, deleted, reserved, and host-only slugs give byte-identical refusals from create and from
      availability;
    - every account in the seeded world lists exactly its own sites and roles;
    - pagination, and every bad query parameter named;
    - a cursor made from A's slug grants nothing;
    - reads by every role, and by the shared account on both sites;
    - non-members in both cases, and a deleted site, answer like no site;
    - a `siteId` in the body names nothing;
    - an idempotent replay works, and one account's key replays nothing to another;
    - a six-way race leaves one site, one owner, one host, and one audit entry, and each loser's answer is a taken
      address's;
    - the save path covers held, hosted, and reserved slugs, a non-address fault that throws, and a landed retry that
      counts as saved;
    - anonymous callers get 401, alike for a real and a missing site.
  - Unconfirmed limit (its own harness, limit 1):
    - one site, then 403 for a free, a taken, and a reserved address alike;
    - an owned site counts, deleted or not, and an Editor role does not;
    - a confirmed account is not limited;
    - six simultaneous creates by one unconfirmed account make one site. This fails three runs in three without the
      lock hint.
  - Startup is refused with a negative limit.
  - Contract and host:
    - every declared status, `Location`, the 409's codes, and the required `slug`;
    - no id in any schema;
    - startup refused without a valid zone;
    - the create limit counts refused creates;
    - the AppHost wiring, local and published;
    - creating a site is the only idempotent route.
- **Reviewers.** `site-isolation-auditor`, `architecture-reviewer`, and `api-contract-checker` found no blocker and no
  isolation major.
  - **Fixed:**
    - Terraform did not set the zone, so a deployed API would not start;
    - a lost commit acknowledgement could answer the creator 409;
    - one shared limit for create, list, and checks;
    - the folder-wide resolver allow-list;
    - rules 2 and 4;
    - the missing-host 500;
    - hand validation in availability;
    - the unnamed query-field error;
    - the 409 and `Location` declarations;
    - the 403 on `SiteAuthor` routes;
    - the cursor message;
    - the missing test cases the auditor listed.
  - **Recorded, not changed:**
    - authorization still runs before the rate limiter, so a refused site read is not counted (PP2.3's order, now
      decided);
    - refusals differ in timing, and only content is compared (PP2.7);
    - the tenant root's tables are joined through the caller's memberships for the list;
    - response schemas mark no member required, as with every existing record; a client-generation concern for PP3.
- **Left for later.**
  - Squatting by many confirmed accounts is left to PP14's site counts and PP19.9's velocity limits. Releasing the slugs
    of sites whose owners never confirmed stays an option for the operator console (PP19).
  - PP2.12:
    - a newly created site's subdomain must resolve at once, with no stale "unknown host";
    - the Site host calls `AddSites` and so needs `Sites:SubdomainZone`.
  - PP14: the entitlements check before creating (step 1).
  - PP17.2: a pending custom host under the platform zone can block a slug; refuse it there.
  - The provisioning prompt after PP9.5: `Provisioning`, `sites.provision`, default theme and settings. Numbered
    PP9.5a at PP2.14.
- **Not verified.** Nothing ran against Azure. The Terraform change is one environment variable.

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default
outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1400 of 1400
(1291 before this prompt). PP2.9's intermittent `IdentityModelTests` (`ManyServiceProvidersCreatedWarning`,
order-dependent) passed in this run and failed in an earlier one; they pass alone.

### PP2.11 Invitations and member management - done

```text
SCOPE: Add invite by email, accept, change role, and remove member endpoints.
CONSTRAINT: SITE-010; Template B.
RESTRICTION: Owner only. Invitations expire and are single-use.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*Split at planning (owner-approved, 2026-10-27):* the prompt needed a new table, the first site outbox message with the
Worker's site resolver, and four endpoints, which is more than one seam (CLAUDE.md). PP2.11a invites and accepts; PP2.11b
lists members, changes roles, and removes them.

### PP2.11a Invite by email and accept - done

```text
SCOPE: Add invite by email and accept an invitation, with the invitation table and its email from the outbox.
CONSTRAINT: SITE-010; Template B and Template C.
RESTRICTION: Owner only to invite. Invitations expire and are single-use. Accepting needs the invited address, confirmed.
BEHAVIOR: Implement with two-site tests.
```

*PP2.11a delivery note (done 2026-10-27):*

- **What shipped.**
  - Two admin routes in `SiteInvitationsController`, which injects only `ISiteInvitationsFacade`:
    - `POST /api/v1/sites/{siteSlug}/invitations` (`SiteOwner`, `[Idempotent]`) answers 202 with `{id, role, expiresAt}`
      and stages the email. The answer is the same whether or not the address has an account, and it never carries the
      address.
    - `POST /api/v1/site-invitations/accept` (signed in, account-level, `[Idempotent]`) answers 200 with the joined site's
      summary.
  - `SiteInvitation`, the first site-owned entity: key `(SiteId, Id)`, a filtered unique index for one pending invitation
    per address per site, check constraints on role, status, acceptance, and expiry, and a rollback that refuses while
    rows exist (51010). Migration `SiteInvitations`.
  - Across the seam:
    - Contracts: `ISiteInvitationsFacade`, `SiteInvitationRequests`, `SiteInvitationId`, `SiteInvitationStatus`, four
      errors, and two audit codes (`site.member-invited`, `site.member-joined`).
    - Business: `SiteInvitationRules` and `SiteInvitationEmails`.
    - Validators for both requests.
    - `SiteInvitationsFacade` for inviting, and `SiteInvitationAcceptance` for accepting, which is the resolver.
    - `SiteInvitationsDataLayer`, `SiteInvitationMailDataLayer`, and `SiteInvitationTokens`.
    - `SiteInvitationRepository`, `SiteMemberRepository.IsMemberAsync`, and two `SitesUnitOfWork` saves.
  - Accounts gained `IAccountAddressCheck`, which answers only about the caller's own address. `EmailAddresses` and
    `EmailLinkOrigin` moved to `Managers/Email` so both modules share them.
  - Worker:
    - `OutboxHandlerRegistration` enters the delivery's site before a handler runs. This is the Worker's resolver, for
      every site message.
    - `AddSitesWorker` registers `SiteInvitationEmailHandler` when email is sent.
    - The AppHost and the Terraform set `Sites__AppBaseUrl` to the admin origin.
  - Rate limits: invites 20 an hour and accepts 20 an hour, per account.
- **Owner-approved decisions:** the split, 7-day links, and acceptance bound to the invited address, confirmed.
- **Defaults (DECIDE, stated in the decision log):**
  - 50 open invitations per site; the address being invited again does not count;
  - non-live sites can still invite and admit;
  - a revoke by re-inviting is not audited on its own.
- **What differs from the plan.**
  - The invite answer has no `email`: idempotent answers are stored, and an address does not belong there.
  - Accepting checks membership before the invitation, so the retry of a lost answer hears `already-member`, as the
    route's description says.
  - From review:
    - acceptance moved into its own file, so the resolver allow-list names only it;
    - the site is entered only once found not deleted;
    - same-address invites take turns under an app lock, with the unique index beneath it (one lock per site since
      PP2.14, under which the cap is counted again);
    - the cap no longer counts the invitation being replaced;
    - a boundary test keeps every file but the Worker's processor from making an outbox delivery, since a delivery now
      enters a site;
    - the message carries the invitation id alone.
- **Tests.**
  - Unit: the rules, the email (encoding, the link fragment, day wording), the token (round trip, altered, other purpose,
    garbage), the validators, address normalization, and the Worker registration.
  - Outbox: a delivery works on its message's site and account work on none, scope by scope.
  - Two-site, over ApiService against SQL Server:
    - `AssertSiteOwnedIsolatedAsync<SiteInvitation>` and the repository's read paths;
    - an invite is filed under its site alone, with its outbox message and audit entry there;
    - only the named site's Owner may invite, and strangers, suspended members, and B's Owner get A's answer as if A
      were not there;
    - re-inviting replaces only that site's invitation;
    - accepting joins only the invited site, and B's tenant root is unchanged, including for B's own Owner;
    - the link works once;
    - only the invited inbox, confirmed, may accept, in any letter case;
    - every unusable link gets one byte-identical answer, a token sealed for B naming A's invitation included;
    - a membership is never changed by an invitation;
    - six simultaneous accepts make one member, and six simultaneous invites leave one open;
    - each site has its own cap;
    - an idempotent replay works;
    - the email is redelivered under one id with a working link, is not sent once closed, and reads only as its site;
    - a refused accept files nothing under either site.
  - Contract: every declared status and code, no site id or address in the schemas, startup refused for bad lifetimes
    and caps, the accept limit, the idempotent route list, the AppHost wiring, and the model and migration lists.
- **Reviewers.** `site-isolation-auditor`, `architecture-reviewer`, and `api-contract-checker` found no blocker.
  - **Fixed:**
    - the rules and the decision log for the two new resolvers;
    - the outbox delivery reachable from anywhere;
    - the accept route's retry description;
    - the cap counting a replaced invitation;
    - concurrent invites leaving two open;
    - the unguarded rollback;
    - the misplaced doc comment;
    - the unused payload field;
    - the incomplete 400 descriptions;
    - the missing two-site cases.
  - **Recorded, not changed:**
    - a live link's refusals differ from a dead one's;
    - closed invitations are kept until PP19 purges them, and PP19.11a must erase by address;
    - the 429 title says "a minute" for the hourly limits, as `sites-create` already did;
    - an Owner can re-invite one address up to the hourly limit, since revoked invitations do not count;
    - `EmailAddressRules` now only forwards to the kernel.
- **Left for later.**
  - PP2.11b: the members list, role changes, and removal, with the 51003 and 1205 answers PP2.3 assigned to PP2.11, and
    the tenant-root two-site tests for a role change or removal on A leaving B's rows unchanged.
  - PP3: the admin app's `/join` page, which reads the token from the fragment and posts it. Moved to
    `/dashboard/join` at PP2.14 (PP-D19: the app's routes live under `/dashboard`).
  - PP14: the member count, checked at accept. PP19: the persisted key ring, and purging closed invitations.
- **Not verified.** Nothing ran against Azure. If `aspire run` was up, the first version of the migration may have
  reached your local database before its index changed in review: drop and re-create the database, or roll the
  migration back (it refuses while it has rows) and forward.

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1467 of 1467
(1400 before this prompt).

### PP2.11b Members list, role changes, and removal - done

```text
SCOPE: Add list members, change a member's role, and remove a member, on the site's admin routes.
CONSTRAINT: SITE-010; Template B; PP2.11a.
RESTRICTION: Owner only. The last active Owner can never be demoted or removed: SiteRoles' rules, the trigger's 51003, and a
deadlock (1205) all answer last-owner. Unknown and other sites' members are indistinguishable.
BEHAVIOR: Plan, wait for approval, implement with two-site tests, including a role change or removal on A leaving B's
rows unchanged (the shared account's above all).
```

*PP2.11b delivery note (done 2026-10-27):*

- **What shipped.**
  - Three admin routes in `SiteMembersController`, which injects only `ISiteMembersFacade`, all behind `SiteOwner`:
    - `GET /api/v1/sites/{siteSlug}/members` answers 200 with a page of `{id, email, role, status, joinedAt, isYou}`,
      ordered by join time and then account. `limit` runs 1 to 100 (default 50) and the cursor is opaque.
    - `PUT /api/v1/sites/{siteSlug}/members/{memberId}/role` with `{role}` answers 204. It is `[Idempotent]`, with the key
      optional.
    - `DELETE /api/v1/sites/{siteSlug}/members/{memberId}` answers 204. It is `[Idempotent]`, with the key optional.
  - New codes: 404 `member-not-found` and 409 `member-changed`. The existing 409 `last-owner` gets its first callers.
  - Across the seam:
    - Contracts: `ISiteMembersFacade`, `SiteMemberRequests`, two errors, and two audit codes
      (`site.member-role-changed`, `site.member-removed`).
    - Business: `SiteMemberCursor`. `SiteRoles`' last-owner rules get their first caller.
    - Validators for the list query and the role request.
    - `SiteMembersFacade` and `SiteMembersDataLayer`.
    - `SiteMemberRepository` gains a keyset page (`FromSql`, keyed by site), a tracked find, an owner count, and a
      remove.
    - `SitesUnitOfWork.SaveMemberChangeAsync`.
  - Accounts gained `IAccountDirectory` (`AccountDirectory`, `AccountAddressRepository`): addresses for the ids given,
    at most 100 a call.
  - Rate limits per account: list 60 a minute, changes 30 a minute.
- **Owner-approved decisions:**
  - the Owner sees members' addresses, through the new Accounts contract;
  - a member's id is their account id;
  - listing is Owner-only, as the prompt says.
- **What differs from the plan.**
  - The domain has no SQL Server package, so SQL error numbers are not read. After a failed save the unit of work asks
    again, as PP2.10 did. A change that would now leave no owner is `last-owner`, which covers both 51003 and 1205.
  - That second read has a `READCOMMITTEDLOCK` hint: a snapshot read could let a deadlock's loser see the site from
    before the winner's commit.
  - From review:
    - A member id is accepted only in the lower-case spelling the list writes, so a key matches one request.
    - Retries of an Owner's own step-down or removal are documented (403 `site-role-required`, 404 `site-not-found`),
      because the site policy runs before the replay.
    - `member-changed` tells the client to use a new key.
    - A fault that outlasted its retries with nothing changed is `member-changed`, not a 500.
    - The audit reason is a fixed `role:{was}->{now}`, not prose.
    - A boundary test keeps `IAccountDirectory` to the member list.
    - A test ties the list's page size to the directory's.
- **Tests.**
  - Unit:
    - the cursor (round trip, offsets, every malformed shape);
    - the validators;
    - both audit entries;
    - the page-size coupling.
  - Two-site, over ApiService against SQL Server:
    - each Owner lists exactly their own site's members, with the shared accounts' own role and standing on each;
    - addresses come from Accounts, and no address of a non-member appears;
    - pages cover every member once, A cursor from B, or the lowest position there is, lists only A's members.
    - every bad query parameter is named;
    - Editors, Authors, and the shared account get 403;
    - suspended members, strangers, and B's Owner get A's answer as if A were not there;
    - B's members, unknown ids, malformed ids, and other spellings are byte-identical 404s through A, with both sites
      unchanged;
    - **a role change and a removal of the shared account on A leave B's rows unchanged**, and the account's next request
      reflects each on A alone;
    - the shared account acting as B's Owner reaches only B's members, and its changes on B leave A unchanged;
    - suspended members keep their standing;
    - a repeat records nothing;
    - the last active Owner can be neither demoted nor removed, and a suspended Owner does not count;
    - an Owner who is not the last may step down or leave;
    - six-round and six-way races leave exactly one Owner, every loser answered `last-owner`;
    - the trigger's refusal is mapped through the unit of work;
    - a forced deadlock that outlasts its retries is answered `last-owner` only after the winner commits;
    - stale reads answer changed, not found, or done;
    - keyed replays of both writes, a key reused on another request, and keyed retries of an Owner's own change;
    - the change allowance.
  - Contract: every declared status, code, and description, the `memberId` description, the idempotent route list, the
    route list, and no site id in any schema.
  - **Checked by planting:** with the lock hint removed, the deadlock test fails three runs in three. The ordinary races
    pass without it: their losers arrive after the commit, and in the race runs 19 losers met the trigger (51003) and
    none deadlocked.
- **Reviewers.** `api-contract-checker`, `site-isolation-auditor`, and `architecture-reviewer` found no blocker and no
  major except the contract checker's one:
  - **Fixed:**
    - retry descriptions that promised a replay an Owner's own change cannot get;
    - `member-changed` advice for the key;
    - the `memberId` description and its two spellings;
    - the list's 400 description;
    - the `email` doc;
    - who may read the directory;
    - the weak cursor check;
    - the shared account acting from B;
    - the "provider-neutral" comment;
    - the 500 after exhausted retries;
    - the prose audit reason;
    - the page-size coupling;
    - staging a role through the data layer.
  - **Recorded, not changed:**
    - two Owners removing one member at once both hear 204, and only the first is audited;
    - the unit of work applies `SiteRoles` itself, as PP2.10's save does;
    - an Owner of two sites sees one account id on both.
- **Left for later.**
  - PP14: suspending and reinstating members for a downgrade (`SiteRoles.CanSuspend` is ready), and member-count limits.
  - PP3: the admin app's members page.
  - A route for an Editor or Author to leave a site, listing and revoking pending invitations, and email to a member who
    was changed or removed are not in any prompt yet.
  - PP19.11a must erase memberships before an account's row goes; until then the list shows `email: null` for it.
- **Not verified.** Nothing ran against Azure. No migration.

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1540 of 1540
(1467 before this prompt).

### PP2.12 PantryPress.Site host and host resolution - done

```text
SCOPE: Configure PantryPress.Site as the anonymous public site API, add it to the AppHost, and resolve Host to
SiteId through a cached SiteHost lookup, with {slug}.localhost support in development.
CONSTRAINT: ARCH-003, SITE-005, SITE-009, PSEC-004; sites.md and public-endpoints.md; add-public-endpoint.
RESTRICTION: No Identity or BFF cookies on this host. Read the site only from Host. No SQL per request once
warm. Reject IP-literal hosts.
UTILIZATION: public-site-reviewer.
BEHAVIOR: Plan, wait for approval, implement with known, unknown, mixed-case, port, www, and two-site tests.
```

*PP2.12 delivery note (done 2026-10-28):*

- **What shipped.**
  - `PantryPress.Site` is the anonymous public site API:
    - It references the database and Redis in the AppHost and waits for both, plus the migration service.
    - Every request except a health probe has its site resolved from the `Host` header. This runs after the
      forwarding-header strip.
    - A Host that names no active site gets the platform's neutral problem 404 (`not-found`, `no-store`). An unknown path
      on a real site gets the same document, whatever `Accept` asks for.
    - Locally a site answers at `{slug}.localhost` on the Site resource's port (SITE-009). The dashboard link says so.
  - Across the seam, in the Sites module:
    - Contracts: `IPublicSiteResolution` and `PublicSite`.
    - Business:
      - `PublicHostNames` splits off the port (1 to 65535, plain digits). It refuses IP literals in every spelling before
        any lookup, and stores the rest in `SiteHostNames`' form.
      - `SiteHostMap` is the cache entry, with its lifetimes and its fill delay.
    - Data layer: `SiteHostMapDataLayer` reads the map, fills it from SQL on a miss, and refreshes it.
    - Facade: `PublicSiteResolution` enters `ISiteContext`. It is the fourth facade resolver on the boundary list.
    - Repository: `SiteHostRepository.FindActiveAsync` (one query, keyed by host, active hosts only) and `ListHostsAsync`.
    - `SitesFacade` refreshes the new subdomain after a site is created (tenant-provisioning G-02, step 6).
    - Registration: `AddSitePublicResolution`.
  - The first cache adapter, brought forward from PP7.12:
    - `IPlatformCache` over `RedisPlatformCache` in `Managers/Caching`. It is the one file that names StackExchange.Redis,
      and it takes only a `PlatformCacheKey`.
    - `CacheConnection` in ServiceDefaults registers the Aspire Redis client. It fails fast, gives each command 500 ms,
      and adds no readiness check.
  - The host map:
    - Keys are `platform:site-host:{host}`. A host that names a site is cached for 10 minutes, one that names nothing for
      60 seconds.
    - Each entry carries a version. A fill writes only while the version is still the one its read saw.
    - The version outlives the value by a day, because the value expires as its own hash field.
    - A lookup that took more than 5 seconds from read to fill caches nothing.
  - The Site host's own pieces:
    - `IPublicSiteContext` must agree with `ISiteContext`.
    - `SiteProblems` writes every refusal and fault in full.
    - The host refuses to start if `Edge:TrustedProxies:ForwardHost` or `AllowedHosts` is set.
    - No Identity, authentication, antiforgery, session, or cookie.
  - ApiService connects to Redis for the refresh and carries on without it.
- **Decided by default (in the approved plan).**
  - No 301 to the canonical host from this API. Resolution reports the canonical host and whether the request used it;
    PP2.13 and PP7.13a answer with the redirect.
  - A Redis outage falls back to SQL and caches nothing.
  - No in-process cache in front of Redis, which would break "a host that changes hands resolves to its new site at once".
- **Tests (154 new; 1694 in all).**
  - Unit:
    - Host reading: ports, every IP spelling, look-alikes, lengths.
    - The map entry: round trip for every status, and every malformed entry read as a miss.
  - Platform cache, over the test AppHost's Redis:
    - a miss, a fill, and lifetimes;
    - a fill refused after one and after two invalidations;
    - the version kept after a short fill expires;
    - every call fails fast when Redis is unreachable.
  - Host resolution and two-site, over SQL and Redis with ApiService and the Site in memory:
    - each site's host resolves that site and enters it;
    - case, a trailing dot, and a port;
    - an unknown host is indistinguishable from an unknown path, under three `Accept` values;
    - IP literals and bare names are refused with zero SQL and zero cache calls;
    - a warm host makes no SQL, and a cold host makes exactly one query;
    - every forwarding header and every host-header trick, both now in `TwoSiteChecks`;
    - a custom domain, its `www` variant, and the old subdomain, each with its canonical host;
    - a pending host is answered like an unknown one;
    - a host changing hands, a canonical host moving, and a status change are each seen at once, with the other site's
      entry byte-identical;
    - without its refresh a write is not seen;
    - a lookup racing a write is refused, and a slow lookup caches nothing;
    - G-02: request, create, request again;
    - a site created with zone `localhost` answers at `{slug}.localhost` on any port;
    - every key is under `platform:site-host:`;
    - no `Set-Cookie`, and no sign-in services;
    - a known host still resolves with Redis unreachable.
  - Edge:
    - the `ForwardHost` and `AllowedHosts` refusals;
    - zero or two Host headers are refused without a lookup.
  - Wiring: the AppHost's references and waits.
  - Boundary:
    - the resolver list;
    - the cache adapter and package lists (one package per project);
    - only the Sites module and the Site host name the public resolver, and only the resolution middleware sets the
      public site.
  - **Checked by planting:**
    - Dropping the refresh after creation fails G-02.
    - Dropping the version condition fails three cache tests and the race test.
    - Letting a fill's lifetime cover the version fails the short-fill test.
- **Reviewers.** `site-isolation-auditor` and `public-site-reviewer` found no blocker. Both raised the same major issue.
  - **Fixed:**
    - Configuration could turn `ForwardHost` on and let a proxy choose the tenant. The host now refuses to start.
    - A fill shortened the version's life, so a very slow lookup could match a restarted count. The version now outlives
      the value, and the fill-delay guard was added.
    - A hung Redis was waited on for 5 s per command. Commands now give up after 500 ms.
    - A non-JSON `Accept` got a plain-text 404 without `no-store`. Every refusal is now written in full.
    - The public site holder and the resolver are now kept to their files by boundary tests.
    - The host-header tricks moved into the harness.
    - Missing tests: two Host headers, a `{slug}.localhost` site end to end, and the `ForwardHost` refusal.
    - Rule text on resolvers, status, and the cache.
  - **Recorded, not changed:**
    - Resolution enters a site whatever its status. The status answers are PP2.13's, owed before any endpoint reads
      site-owned data.
    - The request target and HTTP/2 `:authority` are Kestrel's to reconcile with Host; TestServer cannot show it.
    - `PublicSiteResolution` returns `PublicSite?`, not `Result<T>`, like `ISiteMembershipFacade.FindAsync`.
    - Health probes are mapped in Development only, so a deployed probe gets the neutral 404. This was already so, and
      is PP19's.
    - Redis eviction under memory pressure can drop a version early. The fill-delay guard covers that window.
- **Left for later.**
  - PP2.13: the status answers (SITE-008: 503 with `Retry-After`, 410) and the 301 to the canonical host, from
    `PublicSite`.
  - The first prompt that removes a host or changes a site's status (PP14, PP17, PP19): call `RefreshAsync` or
    `RefreshSiteAsync`, and make the refresh survive a crash through the outbox.
  - PSEC-001: a cap on uncached lookups under a flood of random hosts.
  - PP9.3 and PP19:
    - Superseded at PP2.14 (owner decision): not "Front Door must pass the reader's Host through" and "the ingress
      must accept tenant hosts", but Front Door keeps the origin's Host and overwrites X-PP-Reader-Host, read only
      with the edge secret (PP9.3).
    - Deployed health probes.
  - PP9.5: tenant-provisioning step 7, the edge purge of a new host.
- **Not verified.**
  - Nothing ran against Azure. Azure Managed Redis needs 7.4 or later for the per-field expiry; the local image is 8.6.
  - No migration.
  - **The intermittent race test.** `SiteMembersTwoSiteTests.Owners_stepping_down_at_once_through_the_unit_of_work_leave_one`
    (PP2.11b) failed in both full runs and passed in each of three runs of its class alone. It fails on the untouched commit before this prompt too: a full run of `8bfd0b4` in a separate worktree failed it the
    same way, beside PP2.9's intermittent `IdentityModelTests` and an admin-app start timeout. Under a full run the race
    can deadlock, and the harness's context has no retrying strategy, so SQL Server's deadlock surfaces as EF Core's
    `InvalidOperationException` rather than the `DbUpdateException` or `RetryLimitExceededException` the unit of work
    maps; production's context retries (`EnrichSqlServerDbContext`). Not changed here: it belongs with PP2.11b.

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default
outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1693 of 1694
(1540 before PP2.11b's note; the one failure is the race test above).

### PP2.13 Public site context and status - done

```text
SCOPE: Add GET site context on PantryPress.Site returning public identity and status with SITE-008 and SEO-007
semantics.
CONSTRAINT: SITE-008, SEO-007; add-public-endpoint.
RESTRICTION: No private settings or member data in the response.
BEHAVIOR: Plan, wait for approval, implement with a test per status.
```

*PP2.13 delivery note (done 2026-10-28):*

- **What shipped.**
  - `GET /api/public/v1/site`, the Site host's first route. A site open to readers answers 200 with its name, tagline,
    locale, canonical host, and whether the request used it, cached `public, max-age=60`. Nothing private: no site id,
    slug, time zone, status, setting, or member.
  - The status answers, owner-approved (DECIDE defaults, said out loud):

    | Status | Answer | Code | `Retry-After` |
    | --- | --- | --- | --- |
    | Live, ReadOnly | 200, identical | - | - |
    | Provisioning | 503 | `site-coming-soon` | 30 |
    | Suspended, Offline | 503, identical | `site-unavailable` | 3600 |
    | Deleted | 410 | `site-gone` | - |

    Each refusal is the neutral problem document, `no-store`, with nothing of the site's in it. An unknown path keeps the
    neutral 404 whatever the site's status.
  - Across the seam:
    - Sites Business: `PublicSiteStatus`, the status rule.
    - Sites Contracts: `IPublicSitesFacade`, `PublicSiteDetail`, `PublicSiteIdentity`, and three `SiteErrors`. `ErrorKind`
      gains `Unavailable` (503, with `Retry-After`) and `Gone` (410).
    - Sites Facades: `PublicSitesFacade`. It does no I/O and refuses a site the request did not resolve.
    - The host map entry is format version 2 and carries the identity, percent-encoded, so a warm request makes no
      query. A closed site's entry carries none (`PublicSiteIdentity.Withheld`). A version 1 entry reads as a miss.
  - The Site host:
    - MVC's core only. `SiteStatusFilter` is a global resource filter, so every public controller answers a closed
      site's status before it binds or reads anything.
    - `PublicSurfaceConvention` refuses a route outside `api/public/v1` and, until PP12, any bound request input.
      `EnsureControllersOnly` refuses a minimal endpoint. Both are checked at start.
    - Kebab-case enums. Every route declares 404, 410, 503, and 500. The `public-v1` OpenAPI document is served in
      Development only, behind host resolution.
- **Decided by default (in the approved plan).**
  - No 301 from this API: it reports the canonical host, and the page's 301 is PP7.13a's. This moves the PP2.12 note's
    "301 from `PublicSite`". A closed site's status comes before any redirect.
  - The 200's 60 seconds may show a closed site's name for up to a minute until PP9.5 purges on a status change.
    Refusals are never cached, so a restore is immediate.
- **Differs from the plan.**
  - `canonicalHost`, not `canonicalUrl`: the scheme and port are the edge's, and a local `{slug}.localhost` run's differ.
  - The response record is `PublicSiteDetail`, after the module's nouns (`SiteDetail`), not `SiteContextResponse`.
- **Tests (85 new; 1779 in all).**
  - Unit:
    - the status rule for every status;
    - the facade's answers and its site check;
    - the host map entry: any text and every value the columns allow round-trip, and damaged or old entries are misses;
    - the surface check: routes, bound input, and the cancellation token;
    - kebab-case enums.
  - Over SQL and Redis with the Site in memory:
    - a test per status, with its code, `Retry-After`, caching, and the problem document's fields;
    - live and read-only identical, suspended and offline indistinguishable;
    - a deleted site with no canonical host left;
    - a status change and a restore seen at once;
    - a rename seen at once after its refresh, and not without it;
    - a host that changes hands answering its new site, with no trace of the old one;
    - a closed site's name absent from its cache entry;
    - every host-header and forwarding trick, and a query or header naming a site;
    - a custom domain, its subdomain, and a closed site's alias;
    - a non-JSON `Accept` on a closed site;
    - an unknown path on a closed site;
    - no SQL warm, one query cold, and a row the create validator would refuse still served from the map;
    - 405 for writes, the document in Development only, and no cookie on the route.
  - The problem-code catalogue now reads the Site host and checks that a status has one code on both hosts.
  - **Checked by planting:**
    - Skipping the facade's status check leaves the integration answers right, because the global filter refuses. Only
      the facade's own unit tests fail.
    - Answering Offline as "coming soon" fails four tests, the indistinguishable pair among them.
- **Reviewers.** `public-site-reviewer`, `site-isolation-auditor`, and `api-contract-checker` found no blocker.
  - **Fixed:**
    - A non-JSON `Accept` on a closed site came back as a generic 503 with no code. For a controller's answer, the problem
      details service handed the document to MVC's writer, which rebuilt it. `SiteProblems` now writes the JSON itself.
    - The Site host's 5xx code was `server-error`, where ApiService and the gateway say `unexpected-error`. It is aligned
      now (pre-release), and the catalogue reads the Site host.
    - A row the create validator would refuse - an empty name, a line break, a locale out of shape - was read from SQL on
      every request, because the entry's reader refused it. The reader now checks only format and column lengths.
    - A closed site's name sat in the shared cache. It is withheld now.
    - A deleted site with no canonical host left reported `isCanonical: false` for the host standing in as canonical.
    - The Site host had no guard against binding a site id. It now binds nothing until PP12 brings ApiService's
      `SiteIdBindingConvention` with its first input.
    - A minimal endpoint would have escaped the status answers. The host now refuses one at start.
    - Missing pieces: kebab-case enums, declared 500s, the rename and host-hand-over tests, and the refresh duty for a
      rename in `tenancy.md`.
    - Each test class now shares one SQL counter. A new interceptor per test built EF Core a new internal service
      provider each time, and past twenty EF Core refuses (`ManyServiceProvidersCreatedWarning`). That is what failed
      `IdentityModelTests` in the first full run, and probably PP2.9's intermittent failure of it.
  - **Recorded, not changed:**
    - The name and tagline are the site record's. Once the theme snapshot carries the brand (PP11.4), a page takes its
      branding from the snapshot alone.
    - The SSR server does not call this route per page, since a page makes one page-data fetch. PP7.13's page data
      carries this context.
    - The 200 varies by Host alone, so every cache of it must key on Host or the site id (PP7.12, PP7.13a, PP9.4) -
      since PP2.14 the resolved host, never the inbound Host.
    - During a rolling deploy, old and new Site replicas read each other's entry as a miss. That costs SQL until the
      rollout ends and never crosses sites.
    - `HEAD` is 405.
- **Left for later.**
  - PP7.13 and PP7.13a: carry this context in page data, answer the page's 301, and send the reader's host to the Site
    (superseded at PP2.14: in `X-PP-Reader-Host` with the internal service token, not as the real `Host`).
  - PP8, PP10, and PP11: a change of a site's name, tagline, or locale calls `RefreshSiteAsync` (`tenancy.md`).
  - PP12: bring `SiteIdBindingConvention` and plain-language binding errors to the Site host with the first input, then
    lift the refusal. Refuse reader writes on a ReadOnly site.
  - PP14: the trial-expired answer (SEO-007, BILL-006) is its own decision, not `site-coming-soon` by default.
  - PP7 (G-01): a `Live` site with no page data answers "coming soon".
  - PP9.5: purge the edge on a status change.
  - PSEC-001: a rate limit on this read. It is served from Redis with no SQL once warm.
- **Not verified.** Nothing ran against Azure. No migration.
  - **The intermittent race test.** `SiteMembersTwoSiteTests.Owners_stepping_down_at_once_through_the_unit_of_work_leave_one`
    (PP2.11b) failed in the first and third full runs, the same way as recorded at PP2.12. The second run also failed at
    least 20 persistence schema tests (its listing was cut off there), which passed alone (84 of 84); that run shared
    the machine with another session's work.

Verification: `dotnet build PantryPress.slnx` (0 warnings, separate artifacts path while `aspire run` held the default
outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1778 of 1779
(1694 before this prompt; the one failure is the race test above).

### PP2.14 Tenancy audit - done

```text
SCOPE: Audit PP2 against SITE-001–005, 008–010, ARCH-003, ARCH-015, PSEC-004, and PSEC-007. Report only.
CONSTRAINT: Template L.
UTILIZATION: site-isolation-auditor, public-site-reviewer, architecture-reviewer.
BEHAVIOR: Return findings and checkpoint evidence.
```

*PP2.14 delivery note (done 2026-10-28):* the audit (Template L; `site-isolation-auditor`, `public-site-reviewer`,
`architecture-reviewer`) found no blocker and no way for one site to reach another's data: 2 high, 6 medium, and about 20
low findings. The owner approved "fix all". As at PP1.12, the fix shipped as one change the constitution would have split
(the save fixes, a rate limit, a contract tweak, rules, and the deployment decision), recorded here as one because the
owner asked for one. The review of the fix (`architecture-reviewer`, `site-isolation-auditor`, `public-site-reviewer`,
`api-contract-checker`, then a second look by the isolation and public-tier reviewers) changed it again; what follows is
what shipped. Decision log, 2026-10-28 (PP2.14).

- **High, fixed: a site created under the unconfirmed-account cap could be answered 201 and never saved.** The save
  accepted its changes before its commit, so a commit that failed was retried as an empty transaction and called saved,
  and the idempotency store replayed the 201 for a day. Both saves that open their own transaction now accept changes
  only after commit (`SitesUnitOfWork`). From review: each also looks for its own row only once it holds its lock, so a
  retry racing its own in-flight commit meets its row instead of refusing the creator's own site (over the cap) or
  revoking its own invitation. `A_commit_that_failed_under_a_limit_is_saved_again_on_the_retry` fails without the fix
  (checked by reverting it) and passes with it; `An_invitation_whose_commit_failed_is_saved_again_on_the_retry` is its
  invitation twin.
- **High, decided by the owner: how the reader's host reaches the public tier when deployed.** The rules and the code
  read the connection's Host alone, while the Terraform and PP9.3 had Front Door rewrite it. Asked twice - the second time
  once review showed that the Front Door profile and the Container Apps environment are shared with CreatorPantry, so
  `X-Azure-FDID` and an address prove nothing - the owner chose **hardened forwarding**. Front Door keeps each origin's
  own Host. A rule set on PantryPress's sites routes overwrites `X-PP-Reader-Host` and adds the edge secret
  (`X-PP-Edge`). The SSR server and the Site believe that header only with the edge secret, and the Site also with the
  SSR server's internal service token, compared in constant time and by secret alone. `ForwardHost` stays refused for
  good, and caches key on the resolved host, never the inbound Host. The owner approved amending CLAUDE.md (site isolation
  and the first restriction), ARCH-003, PSEC-004, PP-D03's text, ADR 0008 and ADR 0013, `public-endpoints.md`,
  `tenancy.md`, `rendering.md`, the architecture, and PP7.2a, PP7.13, PP7.13a, PP9.2, PP9.3, and PP9.4. Superseded notes
  are marked where they stand. PP9.3 builds it, with the secret's place in Terraform state recorded as an exception,
  rotation overlap, and its spoofing tests; PP7.13 owns the SSR-to-Site hop. Until then a deployed blog answers the
  neutral 404, as before.
- **Medium, fixed:**
  - *ApiService had no Redis in the Terraform.* It has had Redis since PP2.12 (`locals.tf`, `keyvault.tf`), and the stale
    "no consumer" notes are corrected. Entra ID for Redis is PP19's.
  - *A signed-in caller's refused site lookups were never counted.* Every signed-in request to a `{siteSlug}` route is now
    counted per account before the site policy (`RateLimits:SiteLookupsPerAccountPerMinute`, 300). The 429 is the same
    for a real address and an unheld one. From contract review: the admin document's 429 now says so and declares
    `Retry-After`, and a 429 is a problem document whatever the caller accepts.
  - *Invitation links opened `/join`, outside PP-D19's routes, and read a key of their own.* They now open
    `/dashboard/join` and read `Accounts:AppBaseUrl`, the one key. From review, the account-security link moved to
    `/dashboard/account/security`, and `/forgot-password` joined `frontend.md`'s client-rendered list. PP3.9 now builds
    the join screen.
  - *No prompt owned provisioning or SITE-003's 60 seconds.* PP9.5a now does. A `Live` site with no page data answers
    coming soon (PP7.13, PP7.13a).
  - *The two-site race test was intermittent.* It now runs in contexts that retry, as every host's does. It passed in
    every full run here.
  - *The module's layering differed from `backend.md`.* The save outcomes moved to `Business/`, facades may name neither
    a repository nor a gateway (`ModuleBoundaryTests`), and `backend.md` describes the unit of work as built. A re-check
    under a lock is the same Business rule the facade asked first (`OwnedSiteLimit.Allows`,
    `SiteInvitationRules.HasRoomForAnother`).
- **Low, fixed:**
  - *Ordering and locks.* Invitations take turns under one lock per site (it was per address), named from the site
    context, with the cap counted again under it. The lock gives up after five seconds (51011) rather than carry on.
  - *Startup refusals.* The host refuses a route that names a site after `sites/` by anything but a required
    `{siteSlug}`. The public site refuses a top-level `AllowedHosts` other than `*`.
  - *Boundary tests.* The membership decision is asked only by the site policy's handler. The resolver allow-list names
    the kernel's tenancy files one by one. A site-owned type's key leads with `SiteId`. Hosts query nothing themselves,
    raw ADO.NET included.
  - *New tests.* A trusted proxy, one hop or two, is believed about the reader and never about the host, and an
    untrusted peer about nothing. `X-PP-Reader-Host` names nothing until PP9.3. B's invitations never wait on A's lock.
  - *Smaller changes.*
    - Sites asks `IAccountEmailConfirmation` rather than the publishing check.
    - Accepting an invitation is `SiteInvitationRules.Accept`, decided at one instant.
    - The 413's code and title are one set of constants on all three hosts.
    - The accept request's token is documented as percent-decoded.
    - `tenant-provisioning.md`, the register's SITE-004 row, and the infrastructure README are brought up to date.
    - Owners are named: PP9.8 for the random-host flood, PP12 for a 413 raised while binding a body (today it would be a
      500).
- **Accepted, not changed:** a site route's refusal takes two queries for a live slug and one for none, which tells only
  what availability and the site's own host already say (PP2.7 left the call here). The public site's host-filter check
  also accepts an empty list, which configuration cannot produce.
- **Declined, with reasons:** typed ids in outbox payloads, which are a stored wire format whose handler wraps them (now
  in `backend.md`). Storing an invitation's address once: one copy is the address it is sent to, the other the unique
  index, and PP19 purges closed invitations.
- **Checkpoint (PP2.14):** "Two sites cannot see each other; `demo.localhost` resolves through the site API; members have
  the right powers."
  - *Two sites:* met. The two-site suites pass, every obligation in `tenancy.md` has a named test, and three review
    passes found no isolation hole.
  - *`{slug}.localhost`:* met in process. `SiteHostTests.A_site_at_its_local_address_answers_there_with_any_port`
    creates a site through ApiService with the zone `localhost` and resolves it with any port, any case, and a trailing
    dot; `SiteContextTests` reads the real `/api/public/v1/site`. No `demo` site is seeded, and it was not shown on a
    live `aspire run` (none of PantryPress's was up).
  - *Member powers:* met. `SiteMembersTwoSiteTests` and the policy tests pass, the last-owner guard included.
- **Not verified:**
  - No live `aspire run` demo of `demo.localhost`.
  - Kestrel's own answer to a Host it cannot parse (the in-process test server skips Kestrel).
  - Azure: whether Front Door's rule set overwrites a client's header and can carry the edge secret, its cache key on a
    shared origin Host, and Azure Managed Redis 7.4 or later and its eviction policy.
  - `terraform fmt` and `validate`, with no CLI here.
- **Environment, not code:** one full run had `AdminAppTests.Gateway_serves_the_admin_app_and_keeps_its_own_health_endpoint`
  time out after 5 m 30 s while it started the admin dev server on the real AppHost. The final full run on the same tree
  passed it.

Verification: `dotnet build PantryPress.slnx` (0 warnings; built to a separate artifacts path, as `aspire run` may hold the
default outputs), `dotnet format PantryPress.slnx --verify-no-changes` (clean), and `dotnet test PantryPress.slnx` 1795 of
1795 (1779 before this prompt).

## PP3 — Design system and admin shell

### PP3.1 Design-system decisions - done

```text
SCOPE: Write src/web/PANTRYPRESS-DESIGN-SYSTEM.md: principles, token architecture, the two entry points
(@pantrypress/ui for admin and the platform's own public pages, @pantrypress/ui/themes for public themes),
accessibility rules, and naming. Leave a "The public page" heading for PPL.6 to fill (PP-D19).
CONSTRAINT: PP-D07; design-system.md.
RESTRICTION: Document only.
BEHAVIOR: Show the draft, wait for approval, write.
```

### PP3.2 Token foundation - done

```text
SCOPE: Implement the shared token foundation (color, type, space, radius, elevation, motion) with light and
dark modes, as CSS custom properties exported from @pantrypress/ui.
CONSTRAINT: PP3.1; design-system.md.
RESTRICTION: Tokens are the only place colors live. Self-hosted fonts only.
BEHAVIOR: Plan, wait for approval, implement with a contrast test.
```

*PP3.2 delivery note (done 2026-10-28):*

- **What shipped.**
  - `projects/pantrypress-ui/src/styles/tokens.css`, exported as `@pantrypress/ui/tokens.css` and loaded by
    `pantrypress-admin` ahead of its own styles. It holds every color as an `--L-*` / `--D-*` pair (the owner-approved,
    logo-led palette: teal accent, orange highlight and focus), the active tokens switched by `data-mode` (light, dark,
    and auto in CSS alone; no attribute is auto, and any element may set its own), and the type, space, radius,
    elevation, and motion families.
  - Fonts are system stacks (owner decision): nothing is fetched. PPL.6 brings self-hosted Fredoka and DM Sans and
    switches `--f-display` and `--f-body`.
  - `tokens.spec.ts`, 20 tests. It measures WCAG 2.x contrast for every pair that can meet, in both modes, and requires
    every role to be measured or named exempt. It checks that each role is switched in all three mode blocks and that
    those blocks stay in order. It refuses a literal color outside an `--L-*` or `--D-*` value, in the token file and
    in every other style source of the library and the admin app. It also checks that reduced motion makes every
    duration instant. Each rule was shown failing on a deliberate break. `contrast.ts` is the formula, kept out of the
    package for the specs and PPL.6's focus-ring spec.
- **Differs from the approved plan.**
  - `line-subtle`, a new admin-only role for decorative dividers, as approved. The design-system document records it,
    with `line` held to 3:1 because it outlines inputs.
  - After `design-review`:
    - Reduced motion is `0.01ms`, not `0s`, so `transitionend` still fires.
    - `sideEffects` is `["*.css"]`, so a script import of the tokens survives bundling.
    - `<meta name="color-scheme">` is in the admin page.
    - The literal-color check reaches beyond the token file, and the role-coverage and block-order checks were added.
  - PANTRYPRESS-DESIGN-SYSTEM.md now records:
    - where the tokens live;
    - `data-mode` on `<html>`, reserved from component inputs;
    - the focus ring as an offset `outline`;
    - fills that hold no focusable control, and notices and toasts on `surface`;
    - status icons and borders drawn in `-text`;
    - links underlined in running text;
    - `hl` only under `hl-ink`;
    - which type steps are body text;
    - the deliberate departure from the Studio mockup's names and indigo.
- **Open, for later prompts.**
  - **The `line` drift.** Spec 4.1's public `line` is decorative (about 1.3:1), so a public input border drawn in it
    would fail WCAG 1.4.11. PP7.4 and PP11 settle the public side.
  - **`hl` as a mark on `bg`.** Light `hl` measures about 2.3:1 on `bg`, so it is used only as a fill. A star or icon
    drawn in it on the platform home needs a new role or a darker value, with the owner's approval.
  - **Mode resolution.** It is checked by parsing, not in a browser. PP3.6's Playwright run should read computed
    styles for each mode, nested and under each system preference.
  - **How the spec reads `tokens.css`.** It reads the file from disk, relative to `src/web`. It needs another way if the
    library's tests move to Vitest browser mode.
- **Verification.**
  - Tests: `ng test pantrypress-ui` (20 of 20) and `npm test` (all three projects).
  - Lint and format: `npm run lint` and `npm run format:check`, both clean.
  - `ng build pantrypress-ui`: `styles/tokens.css` and its export are in the package.
  - `ng build pantrypress-admin`, built to a scratch path: the styles bundle is 1.04 kB compressed.
  - Not run: a live `aspire run` or a browser check of the modes.

### PP3.3 Admin primitives: actions and inputs - done

```text
SCOPE: Add admin primitives: button, icon button, text field, textarea, select, checkbox, radio group,
switch, and field wrapper with label, hint, and error.
CONSTRAINT: add-admin-component; design-system.md.
BEHAVIOR: Plan, wait for approval, implement with unit and axe tests.
```

*PP3.3 delivery note (done 2026-10-28):*

- **What shipped.** Nine primitives in `@pantrypress/ui` (`src/primitives/`), exported with a `Pp` prefix.
  - On the browser's own elements, so forms, `routerLink`, and the keyboard work natively:
    - `button[pp-button]`, `a[pp-button]`, `button[pp-icon-button]`, `a[pp-icon-button]`;
    - `input[pp-text-field]`, `textarea[pp-textarea]`, `select[pp-select]`.
  - Custom elements that work in reactive forms, over native inputs: `pp-checkbox`, `pp-switch`, `pp-radio-group`.
  - `pp-field` gives its one control a label, a hint, and an error through `PP_FIELD`.
  - Owner-approved choices:
    - the native-element approach, with the library's lint allowing kebab-case attribute selectors;
    - `disabled` and `disabled-ink` token pairs, exempt from contrast;
    - `axe-core` 4.14.0 as a new devDependency, its only addition;
    - `eslint.ssr-safety.js` over `primitives/`.
  - `@angular/forms` joins the library's peer dependencies.
- **Tests.** 72 library specs, up from 20. They cover:
  - every variant and state, with axe in each;
  - reactive-form read, write, disable, and touched;
  - a busy button or link that ignores a press;
  - caller `aria-*` merged with the field's;
  - `[ngValue]` options;
  - a value the form puts straight back;
  - a server render (`renderApplication`) that gives the same ids twice.

  These were shown failing on a deliberate break: the radio group's sync, the per-application id sequence, and axe
  itself (`axe.spec.ts`).
- **Differs from the approved plan.**
  - After `design-review`:
    - A text control keeps a caller's own `aria-describedby` and `aria-invalid`, joined with the field's. Those inputs
      are aliased to the native names, with a reasoned lint opt-out.
    - Ids come from `PpIdSequence`, one counter per application, so a prerendered page's ids do not depend on which
      worker rendered it and match the browser's.
    - Busy buttons drop hover and press styling.
    - A radio group is touched only when focus leaves the group.
  - The busy guard listens in the capture phase: the caller's `(click)` runs before a host listener, which the first
    run proved.
  - Checked state is written to the element by an effect as well as bound, so a value set and undone before the view
    refreshes still shows truly.
  - "Arrow keys move between radios" is the browser's own behavior over one radio name. jsdom cannot press arrows, so
    PP3.6's browser run proves it.
- **Open, for later prompts.**
  - **Busy is only heard as "unavailable".** The spinner has no words, so a task that must say its progress aloud
    uses PP3.4's live region.
  - **The checkbox has no `error` or `required`.** Sign-up's consent box (PP3.8) needs both.
  - **Forced colors.** A disabled switch looks like an enabled one there. `tokens.spec.ts` forbids system colors in
    library CSS, so a `forced-colors` block needs a decision first.
  - **The busy guard's gaps.** It does not stop an ancestor's capturing handler, a middle-click on a busy link, or
    `requestSubmit()`.
  - **"(optional)" and "Error:" are English.** The admin app's i18n decides how they are translated.
  - **Browser checks for PP3.6:**
    - the switch's `::before` in Firefox and Safari;
    - `:dir(rtl)`;
    - Safari's native select;
    - the unchecked box and radio borders, and `accent-hover`, `accent-active`, and `danger` against `bg` in both
      modes.
  - **The axe helper lives in `src/`,** which the themes entry point may not import, so PP7's reader specs need their
    own.
- **Verification.**
  - Tests: `ng test pantrypress-ui` (72 of 72) and `npm test` (all three projects).
  - Lint and format: `npm run lint` and `npm run format:check`, both clean.
  - Builds: `ng build pantrypress-ui`, and `ng build pantrypress-admin` to a scratch path.
  - Not run: a browser, `aspire run`, or a Playwright flow. There is no screen yet; PP3.6's showcase is the first.

### PP3.4 Admin primitives: layout and feedback - done

```text
SCOPE: Add card, page header, tabs, toolbar, dialog, drawer, toast region, notice, empty state, progress, and
status pill.
CONSTRAINT: add-admin-component.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*PP3.4 delivery note (done 2026-10-28):*

- **What shipped.** Eleven primitives in `@pantrypress/ui`, exported with a `Pp` prefix:
  - `pp-card` and `pp-page-header`;
  - `pp-tabs` and `pp-tab`, in the ARIA tabs pattern;
  - `pp-toolbar`, with roving focus;
  - `pp-dialog` and `pp-drawer`, on the native `<dialog>` with `showModal()`;
  - `pp-toast-region` and the `PpToaster` service;
  - `pp-notice`, `pp-empty-state`, `pp-progress` (on the native `<progress>`), and `pp-status-pill`.

  There are no new tokens. Owner-approved choices:
  - Toasts are `surface` panels.
  - A success or note with no action leaves after 8 seconds, held while hovered or focused. Anything else waits.
  - The toast timer is the library's one timer, with a reasoned per-line lint opt-out.
  - The dialog's focus order on opening.
- **Tests.** 125 library specs, up from 72.
  - Every primitive in each state it has, with axe in each.
  - The keyboard:
    - tabs and toolbar, right to left and with modifiers;
    - the toolbar's single tab stop as controls come, go, and enable;
    - its text fields keeping their own arrow keys.
  - Dialogs:
    - focus on opening and its return;
    - Escape;
    - the backdrop rules;
    - a stale `close`;
    - removal while open.
  - Toasts:
    - stay, hold, and eviction rules under fake timers;
    - focus when a toast goes;
    - timer cleanup.
  - Speech moving into an open dialog.
  - A server render of every primitive.

  Nine checks were shown failing on a deliberate break.
- **Differs from the approved plan.**
  - **The first-focus marker is `ppInitialFocus`.** The template lint refuses `autofocus`, which still works where
    present.
  - **Speaking, after `design-review`.** Everything the library says goes through one polite announcer, the shell's or
    the open dialog's, because a modal makes the page inert and so unheard. The toast list is not live, so toasts no
    longer read out their buttons. A note or success notice that announces is said through the announcer; a warning or
    error is an alert.
  - **Toasts, after `design-review`.**
    - The three-toast limit counts only those that leave by themselves, so one that waits is never dropped.
    - Hover and focus hold separately.
    - Focus moves on when a focused toast goes.
    - The region is a "Notifications" landmark.
  - **The toolbar, after `design-review`.**
    - It roves only between buttons and links.
    - It re-checks its tab stop after every render.
  - **Dialogs, after `design-review`.**
    - An alert dialog is described by its body and never closes on the backdrop.
    - A backdrop close needs the press to start there.
    - The body scrolls by itself.
    - First focus skips controls that cannot take it.
  - **Tabs and the toolbar ignore modified keys.**
  - **Progress fixes.** A max of zero or less shows the bar as indeterminate. Reduced motion holds the bar part-way,
    never full.
  - **`tokens.spec.ts` measures `accent` on `line-subtle`.** That is the progress fill on its track.
  - **Server renders have a spec file of their own.** A server render puts its DOM classes on the global object.
- **Open, for later prompts.**
  - **PP3.6's browser run:**
    - what screen readers actually say for toasts, notices, and speech inside a dialog;
    - the inert page, trapped focus, Escape's `cancel`, and `::backdrop` reading `--overlay`;
    - the dialog body's scrolling;
    - the progress pseudo-elements and the tab list's overflow;
    - `:dir(rtl)` on the drawer;
    - both modes as drawn.
  - **PP3.9: scroll padding.** The shell sets scroll padding for its fixed areas, so no toast or header covers a
    focused control (2.4.11).
  - **Unsaved changes.** Escape and Close have no veto yet. A form that must guard unsaved changes needs one added when
    the first such dialog arrives.
  - **Detection limits.** Right-to-left detection reads `dir`, not CSS `direction`. A `pp-tab` nested in a wrapper
    becomes an orphan panel; the doc says each is a direct child.
- **Verification.**
  - Tests: `ng test pantrypress-ui` (125 of 125) and `npm test` (all three projects).
  - Lint and format: `npm run lint` and `npm run format:check`, both clean.
  - Builds: `ng build pantrypress-ui`, and `ng build pantrypress-admin` to a scratch path.
  - Not run: a browser, `aspire run`, or a screen reader.

### PP3.5 Admin primitives: data - done

```text
SCOPE: Add list shell with paging and sorting, search field, chip group, and an uploader shell.
CONSTRAINT: add-admin-component.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*PP3.5 delivery note (done 2026-10-29):*

- **What shipped.** Four primitives in `@pantrypress/ui`, exported with a `Pp` prefix. They hold no data: each takes
  plain inputs and says what the reader did, and fetching, cursors, debouncing, and sending files stay the app's.
  - `pp-list`, the list shell, around rows the app draws.
    - It is a region named by its `label`, with slots for search, filters, and the empty state.
    - Sorting is a "Sort by" select over typed `sortOptions` with `[(sort)]`.
    - Paging is Previous, Next, and "Page n" (owner-approved: lists page by cursor and know no total, and the app keeps
      the cursors).
    - `status` is `loading`, `ready`, or `error`. The first load shows progress. A later one keeps the rows in view,
      marked busy. An error is an alert with Try again. Each answer after the first says `resultsText` once.
  - `pp-search-field`.
    - A search landmark around a labelled `<input type="search">`, with Clear and Search buttons.
    - `searched` fires on Enter or Search, and with `''` once the field is emptied.
    - Escape with words in the field clears them and goes no further.
    - No debounce (owner-approved), so the toast's stays the library's one timer.
  - `pp-chip-group`.
    - A `fieldset` over native checkboxes, giving an array in the options' order.
    - With `multiple="false"`, native radios instead.
    - A chosen chip has the accent fill and a check mark.
  - `pp-uploader`.
    - A visible button opens the browser's own picker; a drop is the extra.
    - `accept` and `maxBytes` are a courtesy check that names each refused file and why; the server still sniffs and
      scans (MED-001).
    - The app's `items` draw as progress, then Added or Not added, with Try again and Remove.

  There are no new tokens or dependencies. `PANTRYPRESS-DESIGN-SYSTEM.md` has a row for each primitive and a "Data
  primitives hold no data" rule.
- **Tests.** 165 library specs, up from 125. They cover:
  - every state, with axe in each;
  - focus: after paging, after Try again in the list and in the uploader, after Remove (next, previous, button, then
    the uploader itself), after dismissing refusals, and when rows hidden by an error held it;
  - announcements said once: results only after the first answer, and files chosen, refused, added, or failed, never
    progress or a first sight;
  - the courtesy checks (`acceptsFile`, `sizeInWords`), drops while enabled and disabled, and picker reset;
  - chip and search reactive forms;
  - a server render of all four.

  Thirteen rules were shown failing on a deliberate break.
- **Differs from the approved plan.**
  - **The search output is `searched`, not `search`.** `search` is a native event name.
  - **The search field is a `div` with the search role, not a `form`,** so it may sit inside a form.
  - **The chosen sort is bound as an attribute too.** A server render writes no DOM property, and its spec caught the
    first paint showing the wrong sort.
  - **The library's tests now run isolated (`isolate: true` in `angular.json`).**
    - The Angular builder leaves isolation off by default, so `server.spec.ts`'s comment that "Vitest isolates files"
      was never true.
    - A server render's DOM classes leaked into later specs. Toolbar specs failed 2 runs in 4 once the four new files
      changed the order.
    - HEAD was green 5 of 5 in a worktree; with isolation this change is green 5 of 5, at about 3.7 s.
    - The comment now names the setting.
  - **After `design-review`:**
    - **Focus never falls to the page.** Try again leaves focus on the list, or on that file's Remove button. Remove
      moves focus to the next file's Remove, else the previous one's, else the choose button, else the uploader.
      Dismissing refusals moves it to the choose button. Rows or an empty state hidden while holding focus hand it to
      the list.
    - **Paging focuses the rows, not the region.** That puts the reader past the search and filters; the list itself
      takes focus when no rows show.
    - **A choice is "chosen".** Only a finished file is "added", and refusals add "The reason is listed below."
    - **A drop on a disabled uploader is held,** so the browser never opens the file in place of the dashboard.
    - **New inputs and defaults.** The uploader has a `variant` input for a primary choose button. Only the pager button
      pressed shows busy. Loading says "Loading posts" from the label. The chip's check mark keeps its room, so no
      chip moves when chosen. The default `acceptText` is plainer.
- **Open, for later prompts.**
  - **PP3.6's browser run:**
    - a real drop, and `dropEffect`;
    - Escape in the search field inside a dialog (whether a cancelled keydown stops the dialog's close request);
    - Safari's and Firefox's search input with the browser's own clear mark hidden;
    - `:has()` chip states and the check mark in forced colors;
    - focus rings on the list and its rows;
    - both modes as drawn.
  - **The search field copies `control.css`'s look** rather than sharing it, because `control.css` is written for
    `:host`. A change there must be made in both until a shared stylesheet is split out.
  - **The chip group's value type** is `readonly T[] | T | null` for both modes, so a form typed for the other mode
    compiles. The doc says to match it.
  - **Sortable table headings** (`aria-sort`) are not built. Add them when a real table needs them.
  - **The uploader's words are English.** As at PP3.3, the admin app's i18n decides how they are translated.
- **Verification.**
  - Tests: `ng test pantrypress-ui` (165 of 165, isolated, green on five runs in a row) and `npm test` (all three
    projects).
  - Lint and format: `npm run lint` and `npm run format:check`, both clean.
  - Builds: `ng build pantrypress-ui`, and `ng build pantrypress-admin` to a scratch path.
  - Not run: a browser, `aspire run`, a Playwright flow (there is still no screen; PP3.6's showcase is the first), or a
    screen reader.

### PP3.6 Showcase - done

```text
SCOPE: Add a showcase route that renders every primitive in every state and both modes.
CONSTRAINT: design-system.md.
BEHAVIOR: Plan, wait for approval, implement with an accessibility smoke test.
```

*PP3.6 delivery note (done 2026-10-29):*

- **What shipped.**
  - `/showcase` in `pantrypress-admin`, in the development build only.
    - `dev-routes.ts` is empty. The development configuration swaps in `dev-routes.development.ts` through
      `fileReplacements`, and that file lazy-loads `features/showcase/`.
    - A production build carries none of it: a search of its output finds no trace. So the apex's closed list of public
      paths never meets it, and PP3.8's `/dashboard` guard never stands in front of it.
    - `app.routes.ts` spreads in `DEV_ROUTES` first, so PPL.2's not-found route goes after them.
  - Every one of the library's 24 components has a section (`ppa-specimen`).
    - Its states are drawn twice, in a `data-mode="light"` panel and a `data-mode="dark"` one, each a named group.
    - Each panel has its own live state (`ShowcasePanel`). A list page, a search, or an upload "answers" after 600 ms,
      the showcase's one wait, so the working states show.
    - A page-mode control (Follow this device, Light, Dark) sets `data-mode` on `<html>`, and removes it on leaving.
      The page around the panels and the toasts follow it.
  - The page holds the app's one toast region until PP3.9's shell takes it.
  - Owner-approved choices:
    - the development-only route rather than a separate showcase app;
    - both modes side by side;
    - `@playwright/test` ^1.64.1 and `@axe-core/playwright` ^4.14.0 as new devDependencies, run in Chromium,
      Firefox, and WebKit.
  - `npm run e2e` runs `playwright.config.ts`. It serves its own development build on port 4310, so it never meets
    the Aspire-hosted dev server. `e2e/tsconfig.json` type-checks the runs (`npx tsc -p e2e`). Playwright's output
    folders are in `.prettierignore`.
  - `PANTRYPRESS-DESIGN-SYSTEM.md` has a "The showcase" bullet, and the "Accessibility" row of "How the rules are
    kept" now names this run.
- **Tests.**
  - `showcase.spec.ts` (Vitest, 5 tests):
    - every component `@pantrypress/ui` exports is drawn in both panels, so a primitive with no section fails;
    - one toast region, outside the panels;
    - the page mode is set on `<html>` and cleared on leaving;
    - each panel's state is its own;
    - axe in jsdom, with contrast off.
  - `e2e/showcase.e2e.ts`: 25 tests in each of three browsers, 75 runs, green on three runs in a row.
    - axe with the same WCAG 2.x A and AA tags as the library's specs, **color contrast on**. It runs over:
      - the whole page in each page mode, and following a device set to each;
      - every dialog (`md`, `sm`, alert, `lg`) and the drawer, open in each panel;
      - the toasts;
      - the list's busy, narrowed-empty, empty, and error states;
      - the uploader's refusals and a chosen file.
      Contrast was measured on 414 elements, none left undecided.
    - Modes as drawn (PP3.2): each panel's `--bg`, background, and `color-scheme` against `--L-bg` and `--D-bg`, under
      every page mode and device preference, with the page's own body.
    - The native dialog (PP3.4):
      - Tab and Shift+Tab stay inside, and a script cannot focus anything behind it;
      - Escape closes it, and focus goes back to the opener;
      - `::backdrop` is `--overlay`;
      - an alert starts on Keep it and ignores the backdrop;
      - a dismissible dialog closes on a backdrop press.
    - The keyboard:
      - the radios' arrow keys (PP3.3);
      - the focus ring as an offset outline in `--focus`, in each panel;
      - focus kept when the tip is dismissed and brought back.
  - These were shown failing on a deliberate break:
    - a low-contrast `--muted` in the dark panels (47 contrast violations);
    - a backdrop not drawn in `--overlay`;
    - a dark panel drawn light;
    - outlines removed;
    - a section taken out (the coverage spec named the toolbar in both modes).
- **Differs from the approved plan.**
  - **The dialog focus-return test opens its dialog from the keyboard.** WebKit never focuses a button on a click, so
    a dialog opened by mouse there has no opener, and focus falls to the page on closing. That is a library finding
    (open, below), not a showcase one.
  - **After `design-review`:**
    - The list's pager shows only while it has rows. Before, the empty and error states drew an enabled Next.
    - The list's empty state has its one next step. Narrowed, it is "No recipes match" with Show every recipe, which
      clears the search and filter. Otherwise it is "No recipes yet" with Write a recipe.
    - The dismissible tip is there at load, so it no longer announces; only its return does. Dismissing it moves focus
      to Show the tip again, and bringing it back moves focus to its Dismiss button.
    - States added:
      - a `lg` dialog;
      - a field with a hint and an error together;
      - a finished progress bar;
      - the max-of-zero bar, renamed for what it shows.
    - Copy:
      - the alert dialog now moves the post to the bin, as the toast's Undo implies;
      - the Diet chips list diets;
      - the time zone reads "Central Time (Chicago)".
    - Each panel is a group named "Button, light" and so on. The headings take `--display-weight` and
      `--display-track`. The e2e asserts one toast region on the page.
- **Open, for later prompts.**
  - **WebKit and mouse-opened dialogs (PP3.10 to decide).** `PpModalBase` takes its opener from `activeElement`, which a
    click in Safari never sets. A keyboard or screen-reader user gets focus back; a mouse user in Safari does not. A fix
    would record the pressed control from the event, as a library change with its own spec.
  - **Rules the smoke test does not run.** axe runs the WCAG tags only, so the best-practice `heading-order` and
    `landmark-unique` do not run.
    - The showcase repeats each landmark name once per panel by design.
    - The page-header specimens add `h1`s under `h2`s, and the shell's `<h1>PantryPress</h1>` stays until PPL.2
      rewrites it.
    - PP3.9's shell is where a page's real structure gets those rules.
  - **Still for a person or a later run:**
    - what screen readers say;
    - a real drop and `dropEffect`;
    - forced colors;
    - `:dir(rtl)`;
    - real Safari (WebKit on Windows is the engine only);
    - the tab list's overflow;
    - Escape in a search field inside a dialog;
    - the switch's `::before` and Safari's native select as drawn.
  - **PP3.9:** the toast region moves to the shell, and the showcase's own region goes then.
  - **No CI yet.** There is no workflow in the repository, so `npm run e2e` is run by hand. `e2e/` is type-checked
    and formatted but not under ESLint, which lints the three projects only.
- **Verification.**
  - Tests:
    - `npm test`: 165 library, 6 admin, 1 site;
    - `npm run e2e`: 75 of 75 in Chromium, Firefox, and WebKit, three runs in a row;
    - `npx tsc -p e2e`.
  - Lint and format: `npm run lint` and `npm run format:check`, both clean.
  - Builds:
    - `ng build pantrypress-ui`;
    - `ng build pantrypress-admin`, development and production, to scratch paths. The production output has no
      showcase code.
  - Not run: `aspire run` (the e2e serves its own build), or a screen reader.

### PP3.7 Runtime configuration and typed API base - done

```text
SCOPE: Add runtime configuration and a typed API base service in pantrypress-admin.
CONSTRAINT: frontend.md.
RESTRICTION: Components never inject HttpClient.
BEHAVIOR: Plan, wait for approval, implement.
```

*PP3.7 delivery note (done 2026-10-29):*

- **What shipped.** `core/` in `pantrypress-admin`. There are no new dependencies and no screens.
  - `core/api/`:
    - `api-constants.ts`: `API_BASE` (`/api/v1`) and the antiforgery names. These are constants, not runtime values,
      because the app and the gateway share one origin (PP-D19).
    - `api-problem.ts`: `ApiProblem` and `ApiResult<T>`. A failure is a value. The client reads the problem document's
      `code`, `title`, `errors`, `traceId`, and `Retry-After`, and takes the status from the answer itself. It adds two
      codes of its own: `unreachable` when no answer came, and `unexpected-error` when an answer came that is not one
      of ours.
    - `api-path.ts`: `ApiPath`, `toApiPath`, and the `apiPath` template. Each spliced value is encoded as exactly one
      segment. A leading or doubled slash, a query, a fragment, and a dot segment in any spelling are refused.
    - `api-client.ts`: `ApiClient`, the one class that injects `HttpClient`.
      - It has `get`, `post`, `put`, `patch`, and `delete`, each answering a `Promise<ApiResult<T>>`.
      - It sends `Idempotency-Key` only when its caller gives one, checked as `ApiIdempotency.TryParseKey` reads it.
      - It sends a write again once, only after the gateway's own 400 `antiforgery-failed`.
    - `provide-api.ts`: `withFetch()` and `withXsrfConfiguration` over `__Host-pp-xsrf` and `X-PP-Xsrf`.
  - `core/config/`:
    - `RuntimeConfig`, a typed document with no fields yet. PPL.13 adds `publicSignUp`, defaulting to `false`.
    - `readRuntimeConfig`, which reads known fields only and defaults the rest.
    - `RuntimeConfigService`, with `config` and `status` signals. It reads once, in the browser only, and never holds
      up the app's start (`provideRuntimeConfig`).
  - **The owner-approved choice (option A).** The configuration is read from `GET /api/v1/client-configuration`, and
    PPL.13 adds that ApiService route with its first field. Until then the read gets a 404 and the defaults stay.
  - **The admin `eslint.config.js`.** Outside `core/api/`, no file may import `@angular/common/http` or its subpaths.
    No admin file may use `fetch`, `XMLHttpRequest`, `EventSource`, or `WebSocket`, bare or through `window`,
    `globalThis`, or `self`, or `navigator.sendBeacon`. The PP-D07 themes boundary is kept in both blocks.
- **Tests.** The admin project has 35 specs, up from 6.
  - `api-client.spec.ts`:
    - paths, queries, and a 204;
    - no `X-PP-*` header but the write token, and no cookie header;
    - the JSON body;
    - idempotency keys, refused and accepted as the server parses them;
    - the token echoed on a write and not on a read;
    - the one retry with the fresh token, giving up after it, and no retry for a read, another 400, or another
      status;
    - reading a problem: field errors, `Retry-After`, the trace id, the status from the answer, a code with no title,
      and an empty `errors`;
    - an unreadable body and no answer;
    - path shapes, encoded dots included.
  - `runtime-config.service.spec.ts`: defaults then the read, a 404 and no answer, a body that is not an object, one
    read for many callers, and no read on the server.
  - Shown failing on a deliberate break:
    - the retry turned off (2 failures);
    - `withXsrfConfiguration` removed (2 failures);
    - a component importing `HttpClient` and `HttpTestingController` and calling `fetch` (3 lint errors);
    - `window.fetch`, `navigator.sendBeacon`, `new WebSocket`, and `globalThis.XMLHttpRequest` (4 lint errors);
    - a themes import inside and outside `core/api/` (both refused).
- **Differs from the approved plan.**
  - **After `design-review`:**
    - Dot segments are refused when escaped too (`%2e%2e`, `.%2e`, `%2e.`).
    - The lint net is wider: property access, `EventSource`, `WebSocket`, and `sendBeacon`.
    - The service's doc says screens draw from `config()` alone. `status` stays `loading` on the server, so a waiting
      state drawn from it would prerender and then shift.
  - **After `api-contract-checker`:**
    - The key check matches the server's: one pair of wrapping quotes comes off, and `"`, `\`, and `,` are refused.
    - A problem with a `code` but no `title` keeps its code and takes a plain title. A trace id is kept whenever the
      body has one.
    - An empty `errors` reads as none.
    - The no-`X-PP-*` claim names the write token as its one exception, and its spec sets the cookie.
  - **Docs reconciled (CLAUDE.md: documents that disagree are reconciled before the code).** PPL.15 said
    `/api/v1/plans` would be the only anonymous route outside the account routes. It now names both. PPL.13 has an
    "Owed by PP3.7" note: the route's seam, record, `Cache-Control`, its own per-address limit (never
    `account-anonymous`), its pins and contract test, and a CTA drawn from `config()` that hydrates eagerly. The
    decision log has the entry.
- **Open, for later prompts.**
  - **Until PPL.13, every browser start logs a 404.** The read reaches ApiService on every page load, the landing page
    included, and shows in the console and the API's logs. This is accepted rather than hidden behind a build flag.
  - **The retry relies on `antiforgery-failed` being the gateway's alone.** It is today: ApiService names it only in
    OpenAPI prose. No catalogue test pins that ApiService and the Domain never declare it (or `cross-origin-blocked`).
    PP3.10, or the next prompt that touches `ProblemCodeCatalogueTests`, should add one.
  - **Over plain HTTP the retry is wasted.** The gateway refuses without re-minting there, so the second try fails the
    same way. Nobody meets the admin app over plain HTTP (PP1.8).
  - **Hydration (PPL.2, PPL.13).** Once the apex prerenders, the read usually lands after the first hydration. A switch
    must not sit in a lazily hydrated block.
  - **No cancellation.** A call answers a promise and cannot be cancelled. An opted-in write runs to its end anyway
    (PP1.9). Add an `AbortSignal` option when a screen needs to drop stale reads, such as a search.
- **Verification.**
  - Tests: `npm test` (165 library, 35 admin, 1 site) and `npm run e2e` (75 of 75 in Chromium, Firefox, and WebKit).
  - Lint and format: `npm run lint` and `npm run format:check`, both clean.
  - Builds: `ng build pantrypress-admin`, development and production, to scratch paths. The production initial total
    is 59.35 kB compressed.
  - Not run: `aspire run`, or a read through the real gateway, which has no route to answer yet.

### PP3.8 BFF auth client and guards

```text
SCOPE: Add the BFF session client, route guards, and the sign-in, sign-up, and verify-email screens at
/sign-in, /sign-up, and /verify-email on the platform home, with a guard that sends an anonymous visitor at
/dashboard/… to /sign-in and a signed-in one from those screens to /dashboard.
CONSTRAINT: ACCT-001, ACCT-002; PP-D19 (ADR 0019); the route table PPL.2 writes; Template H.
RESTRICTION: Runs after PPL.1 to PPL.3. Sign-up honours the PPL.13 gate (BIZ-009). No screen on another host.
BEHAVIOR: Plan, wait for approval, implement.
```

### PP3.9 Admin shell and site switcher

```text
SCOPE: Add the admin shell (navigation, header, account menu) under /dashboard and a site switcher that sets the
route's site (PP-D19), and the /dashboard/join screen an invitation's email links to: it reads the token from the
fragment, keeps it through sign-in or sign-up, and accepts it (PP2.11; path fixed at PP2.14).
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
CONSTRAINT: POST-006; ACCT-001 (an unconfirmed email address is a blocking item, from
IAccountPublishingEligibility, which PP1.2c built for this).
BEHAVIOR: Plan, wait for approval, implement with a test per item.
```

### PP5.14 Publish, unpublish, and published revisions

```text
SCOPE: Implement publish (immutable published revision of the post and referenced recipes), unpublish, and
republish, raising an outbox event for page-data builds.
CONSTRAINT: POST-005, PP-D04; ACCT-001 (publish and schedule ask IAccountPublishingEligibility and refuse
email-not-verified); Template D.
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
RESTRICTION: Node holds no secrets beyond the internal service token and, from PP9.3, the edge secret (PP2.14), and
has no SQL, Redis, or blob access.
It forwards the host the reader asked for to the site API - in X-PP-Reader-Host, with the internal service token
(PP7.13; PP2.14) - and never chooses a site itself.
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
SCOPE: Add the internal page-data endpoint in PantryPress.Site: given the reader's host - which the SSR server sends
in X-PP-Reader-Host with the internal service token, and which the Site's host resolution reads on this call only with
that token (public-endpoints.md; this prompt owns that hop, locally and deployed) - and a path, resolve
the site, follow the live pointer, and return that route's page data from Redis, falling back to blob, with
ETag. A Live site with no page data yet answers 503 site-coming-soon, as Provisioning does (tenant-provisioning
G-01, until PP9.5a provisions every new site).
CONSTRAINT: ARCH-003, ARCH-007, ARCH-013, PNFR-002, SITE-008; add-public-endpoint.
RESTRICTION: Callable only by pantrypress-site with the internal service token, compared in constant time; an empty
or unset token matches nothing. Its path is also outside Front Door's site-API route, because that route lists the
public paths one by one rather than /api/public/* (this prompt changes site_api_paths in the Terraform; PP9.2 keeps
it so).
No SQL on the request once the host map is warm. Paths are normalized; traversal is impossible.
BEHAVIOR: Plan, wait for approval, implement with tests, including serving with SQL stopped.
```

### PP7.13a SSR request path

```text
SCOPE: In pantrypress-site, wire route resolvers to the page-data endpoint with an in-process LRU, set
Cache-Control for edge caching, and render the neutral 404 for unknown hosts and the SITE-008 and SEO-007
status pages (503 with Retry-After, 410), the coming-soon page for a Live site with no page data yet included (G-01).
The SSR server reads the reader's host only as public-endpoints.md allows (locally the Host; behind Front Door the
edge-proven X-PP-Reader-Host) and passes it on with the internal service token; it never chooses a site. The
in-process LRU and every other cache key on the resolved host or site id, never on the inbound Host, which every tenant
shares behind Front Door, with a two-site test that A's entry is missed as B when both arrive with the same
connection Host. Angular SSR's allowedHosts is ['*'] and trustProxyHeaders stays off; canonical and og:url addresses
come from page data, never from the request URL. The page's 301 to the canonical host is answered here.
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
SCOPE: Write the Terraform platform stack for the resources shared with CreatorPantry (PP-D16): the Front Door
Standard profile, the Container Apps environment with its VNet, and the Log Analytics and Application Insights
workspace, for the non-production environment. Document how CreatorPantry references them as existing.
CONSTRAINT: PP-D13, PP-D16, OPS-001; docs/infrastructure plan and runbook.
RESTRICTION: Shared resources only: no SQL, Redis, storage, Key Vault, or DNS here. No production apply. No
credential literals.
BEHAVIOR: Show the plan and cost, wait for approval, generate, validate with terraform plan, and report.
```

Starting point (2026-10-24): `docs/infrastructure/terraform/platform/` already exists, validated but never applied
(OPS-001 and D-16 closed). This prompt reviews it against the scope, runs `./run.ps1 platform dev plan`, and
applies dev after approval. The privatelink DNS zones live here too, because a VNet can link only one zone of each
name.

### PP9.1a Azure DNS and wildcard record

```text
SCOPE: Write the IaC for PantryPress's own pantrypress.app Azure DNS zone, the wildcard CNAME to the Front Door
endpoint, the apex alias record for the platform home, and the www and app records Front Door redirects to the
apex, for the non-production environment.
CONSTRAINT: PP-D13, PP-D19, SITE-003; docs/infrastructure plan and runbook; OPS-001.
RESTRICTION: No production apply. No credential literals.
BEHAVIOR: Plan, wait for approval, generate, validate with terraform plan, and report.
```

Starting point (2026-10-24): `docs/infrastructure/terraform/pantrypress/dns.tf`. The zone is per environment.
Dev defaults to the child zone `dev.pantrypress.app`, delegated by NS records; prod uses `pantrypress.app`. A
separate registrable domain for dev is the alternative, and keeps dev's blogs cross-site from its admin app as in
prod. Reserve the `dev` slug. Since PP-D19 the zone's apex is this stack's (the platform home), not a marketing
site's: `dns.tf`'s comment saying otherwise is replaced, and an apex takes an alias record, never a CNAME.

### PP9.2 Front Door endpoints, routes, and wildcard TLS

```text
SCOPE: Add IaC for PantryPress's endpoints and routes on the shared Front Door Standard profile: a wildcard
custom domain *.pantrypress.app (managed or Key Vault certificate per current support), the apex pantrypress.app
for the gateway (the platform home, PP-D19) caching only fingerprinted static assets and never a document,
www.pantrypress.app and app.pantrypress.app as 301 redirects to the apex keeping path and query, two origin groups for blogs (pantrypress-site for page routes; PantryPress.Site for
its public paths listed one by one - never the page-data path (PP7.13) - sitemaps, robots.txt, feeds, ads.txt, and
the search index), a PantryPress WAF policy, and
Container Apps ingress IP restrictions allowing only the AzureFrontDoor.Backend service tag.
CONSTRAINT: PP-D13, PP-D16, PP-D19, PSEC-001; docs/infrastructure plan and runbook.
RESTRICTION: Standard tier only (PP-D13); no Premium-only features such as Private Link or managed rule sets.
Never modify CreatorPantry's endpoints or routes. Origins accept traffic only from Front Door's address ranges, a
filter; what proves a request is PantryPress's own is PP9.3's edge secret, never X-Azure-FDID (PP2.14). No
production apply.
BEHAVIOR: Verify current Front Door wildcard certificate options, plan, wait for approval, generate and validate.
```

Starting point (2026-10-24): `docs/infrastructure/terraform/pantrypress/frontdoor.tf` and `apps.tf`. Verified
then: managed certificates cover wildcard domains (GA June 2025) but do not rotate on their own, so the stack
outputs `certificate_expiry`. An external workload-profile environment cannot filter inbound traffic with an NSG,
so the service-tag restriction is each app's ingress allow-list, built from the `AzureFrontDoor.Backend` tag's
IPv4 ranges. Caching is off on every route until PP9.4. Standard's limit of 100 custom domains per profile is
D-13b.

### PP9.3 Origin trust check

```text
SCOPE: Build the owner's PP2.14 decision (public-endpoints.md, "How the reader's host reaches this tier"). In
Terraform, a rule set on PantryPress's sites routes that overwrites X-PP-Reader-Host with the requested hostname and
sets X-PP-Edge to the edge secret from Key Vault, with rotation. In PantryPress.Site's host resolution, read
X-PP-Reader-Host only when X-PP-Edge matches the edge secret in constant time (the SSR hop's token path is PP7.13's),
and answer the neutral 404 to any deployed request with neither proof, health probes excepted. In pantrypress-site,
read the reader's host the same way before passing it on.
CONSTRAINT: PSEC-001, PSEC-004, ARCH-003; edge hardening (PP1.8); public-endpoints.md; tenancy.md rule 5.
RESTRICTION: By secret only: no address, subnet, Front Door range, or X-Azure-FDID (the profile is shared, PP-D16)
stands in for it. The SSR server makes the same check before it passes the host on, holds the edge secret beside its
token, and never forwards X-PP-Edge. An empty or unset secret matches nothing; a non-Development host without one
refuses to start unless it says on purpose it runs without the edge (the TrustNobody pattern). During a rotation
both hosts accept the current and the previous value. The rule set's header value is a literal, so it sits in
Terraform state and is readable by anyone with Reader on the shared profile: record that in the infrastructure
README's exceptions; it tells PantryPress's routes from others, not one operator from another. Never logged or
rendered. ForwardHost and Edge:TrustedProxies:AllowedHosts stay refused; X-Forwarded-Host never names a host.
Exactly one X-PP-Reader-Host value; a list or a repeat is refused. Front Door keeps sending the origin's own Host, so
no tenant host or custom domain is bound at Container Apps. Verify, and record, that the rule set's overwrite replaces
a value the client sent.
UTILIZATION: site-isolation-auditor, public-site-reviewer, integration-reviewer.
BEHAVIOR: Plan, wait for approval, implement with spoofing tests: X-PP-Reader-Host sent straight at the origin with
and without a forged or stale edge secret, a client's own X-PP-Reader-Host through a genuine Front Door hop, a list
and a repeated header, a valid FDID from another route or profile, X-Forwarded-Host and its kin, and every two-site
host trick through the proven path.
```

### PP9.4 Caching rules

```text
SCOPE: Define Cache-Control per response (SSR HTML with a long shared TTL and explicit purge, short browser
TTL; assets immutable; feeds and sitemaps short) and Front Door rule sets that honor them and vary only on
host. Resolve the PP-D02 DECIDE: verify whether Front Door can serve stale content on origin errors.
CONSTRAINT: PP-D02, PNFR-002, ARCH-007.
RESTRICTION: Dynamic island endpoints are never cached at the edge unless explicitly marked. Cache keys always
include the domain the reader asked for: verify Front Door keys on the requested domain, never on the origin host
header, which every tenant shares (PP2.14).
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

### PP9.5a Site provisioning

```text
SCOPE: Close tenant-provisioning G-01. A new site starts Provisioning; a sites.provision outbox job gives it its
default design and settings, builds its first page data, purges its host, and moves it to Live, refreshing the host
map through the outbox. Prove SITE-003: a site created through the API answers over HTTPS at {slug}.{zone} within 60
seconds.
CONSTRAINT: SITE-003, SITE-008; tenant-provisioning.md; tenancy.md ("Tenant root": a status change refreshes the host
map); add-background-job.
RESTRICTION: Idempotent and resumable: a provisioning job that runs twice leaves one site, one first build, and one
purge. Until PP11.4's theme snapshot exists, the default design is the built-in one. One seam; split if the design
defaults need their own.
UTILIZATION: site-isolation-auditor, architecture-reviewer.
BEHAVIOR: Plan, wait for approval, implement with tests, including a timed check against a deployed environment.
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
or stricter, and that no PantryPress host can set or read it. Since PP-D19 the cookie lives on the apex: prove
in Chromium, Firefox, and WebKit that it is kept and sent there with pantrypress.app treated as a public suffix,
and never sent to a blog subdomain, before the Public Suffix List entry is submitted.
CONSTRAINT: PSEC-003, PP-D13, PP-D19 (ADR 0019); gateway.md and auth.md.
RESTRICTION: Do not break existing sign-in. Verify antiforgery still works.
BEHAVIOR: Plan, wait for approval, implement with cookie-tossing tests from a sibling subdomain.
```

### PP9.8 WAF custom rules and rate limits

```text
SCOPE: Add a Front Door Standard WAF policy with custom rules (rate limits per client IP, geo or IP block
lists, and blocks for obviously malicious paths), plus application rate limits on
public dynamic endpoints per IP, per site, and global, and a global cap on host-map lookups that miss the cache, so a
flood of random hosts under the zone cannot drive SQL (PP2.14).
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
SCOPE: Add a typed gateway for the spam challenge (default Cloudflare Turnstile) server-side verification, and
require it on account sign-up as well as on reader forms.
CONSTRAINT: ENG-003, ACCT-003, BIZ-009 (on sign-up before public sign-up opens; PP1.2 deferred it here); Template F.
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
CONSTRAINT: PP-D14; Template F. Registered in the Worker, which sends account email from the outbox (PP1.10b), so the
Worker's AddAccountsWorker sendsEmail becomes true wherever the gateway is configured.
RESTRICTION: No bulk or broadcast capability is exposed. No live call in default CI. Deployed account email needs
ApiService's key ring persisted and shared with the Worker first (PP19): the Worker makes the links ApiService checks,
and refuses to start sending outside Development without it (PP1.12).
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

The landing page's pricing reads this catalogue through PPL.15's anonymous route, so the catalogue carries what a
plan card needs (key, name, monthly and annual price, the limits, an optional trial length, and which plan is
recommended) and can be published or withheld by configuration. `docs/features/plans/` holds the recommended
tiers and figures (D-09, D-10); they become configuration here, never code.

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

Close D-13b here (opened 2026-10-24). Front Door Standard allows 100 custom domains per profile, so the
entitlement check also counts custom domains platform-wide and per profile. The default trigger for a second
profile, or Premium, is 80.

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
CONSTRAINT: OPS-001, PP-D16; docs/infrastructure plan and runbook.
RESTRICTION: No production apply in this prompt.
BEHAVIOR: Plan, wait for approval, generate, validate with terraform plan, and report.
```

Starting point (2026-10-24): the `platform` and `pantrypress` stacks already carry `env/prod.tfvars`, with private
endpoints, WAF, delete locks, and 35-day restore. This prompt replaces the placeholder images, maps the HTTP
probes, persists the data-protection key ring (which lifts the gateway's single-replica limit), and confirms the
sizes against PP19.1's cost estimate.

Owed since PPL.3 (2026-10-29): the gateway serves the admin app's build itself and refuses to start without one. Its
image carries `dist/pantrypress-admin/browser/*` at `/app/admin` (`Gateway:AdminApp:Root`'s default, `admin` under the
content root), or the deployment sets `Gateway__AdminApp__Root`. The HTTP probes this prompt maps must be the real
health endpoints: until then `/health` and `/alive` answer the gateway's 404 outside Development.

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

Owed since PPL.3 (2026-10-29): the gateway's image is built after `npm run build` (which checks the admin build with
`check:admin-build`) and copies `src/web/dist/pantrypress-admin/browser/*` to `/app/admin`. A gateway image without it
refuses to start, which the health-gated rollout should report as the reason.

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

### PP19.11a Account deletion and export

```text
SCOPE: Add self-service account deletion with a grace period, and a self-service export of the account and the
sites it owns, through the account API.
CONSTRAINT: ACCT-004, OPS-011, BIZ-006; auth.md; Template B. DECIDE: the grace period.
RESTRICTION: Deletion asks for the password and, with two-step sign-in, a code; it signs out every session (rotates
the security stamp) and is reversible until the grace period ends. Content is never lost inside it. The export is
no-store and never reaches a log. Owned sites follow the site deletion path; the audit log's pseudonymous ids follow
OPS-011's decision.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

*Added at PP1.12 (owner-approved "fix all", 2026-10-24):* the audit found ACCT-004 listed for PP1 and PP19 but built by
no prompt. PP19.12 extends the export this prompt creates.

### PP19.12 Reader data requests

```text
SCOPE: Add erasure of a reader's comments, ratings, and subscriptions across a creator's sites, and extend
creator export to include site content and subscriber CSV.
CONSTRAINT: OPS-011, ACCT-004, BIZ-006, CMP-002.
BEHAVIOR: Plan, wait for approval, implement with tests.
```

### PP19.13 Platform legal pages

```text
SCOPE: Publish platform Terms, Acceptable Use, Privacy Policy, DPA, sub-processor list, and DMCA agent page as
markdown in docs/legal/, rendered on the platform home by PPL.19's legal pages (/terms, /privacy,
/acceptable-use, /dmca, and the rest), so the landing page's footer links stop saying "(coming soon)".
CONSTRAINT: CMP-001, CMP-002; PP-D19; PPL.19.
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
SCOPE: Audit the whole PantryPress release against every requirement ID, the landing page's PPL-* included.
Report only.
CONSTRAINT: Template L; docs/prompts/pantrypress-landing-page-scrub-prompts.md.
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
RESTRICTION: SiteId from the reader's host only (public-endpoints.md). No site identifier in the payload. Rate-limited. Untrusted input. Works
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
| ACCT-001–003, 005; PSEC-003 | PP1 |
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

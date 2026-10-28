# CLAUDE.md: PantryPress

PantryPress is a standalone, hosted, multi-tenant food-blogging platform: a "WordPress for food bloggers"
with everything built in. Bloggers write posts and structured recipes and publish them to a fast,
SEO-ready site at `{name}.pantrypress.app` or an opt-in custom domain.

This file is the constitution. Detail lives in `.claude/rules/`, procedures in `.claude/skills/`, reviewers
in `.claude/agents/`. The functional source of truth is `docs/requirements/pantrypress-requirements.md`;
site design detail is in `docs/requirements/pantrypress-customization-spec.md`; the build plan is
`docs/prompts/pantrypress-scrub-prompts.md`, with the platform's own landing page in
`docs/prompts/pantrypress-landing-page-scrub-prompts.md` (PPL.*). Decisions are recorded in
`docs/architecture-decisions/pantrypress.md` (created by PP0.1). If documents conflict, stop and reconcile
them before writing code.

---

## Scope

**In scope (the closed feature set, requirements section 2.4):**

- Block editor for posts and site pages; structured recipes with paste-to-parse, deterministic scaling,
  unit and temperature conversion, and schema.org JSON-LD.
- Publishing as immutable snapshots, page data, and Angular SSR rendering cached at Azure Front Door.
- Site design through typed settings only: presets plus the Studio customizer (PP-D17).
- Homepage and site pages built from a shared section library and page blocks, never a page builder (PP-D18).
- Media library, SEO and discovery, ratings, comments, newsletter signup with CSV export, contact form,
  AdSense and allow-listed ad networks, affiliate disclosure, cookie-free analytics, Stripe subscriptions,
  custom domains, the publishing API, and WordPress import.

**Out of bounds. If a task drifts here, stop and ask:**

- Any AI or model call anywhere in PantryPress (PP-D12). AI belongs to CreatorPantry.
- Plugins, third-party themes, custom CSS, HTML, templates, scripts, or font uploads.
- Drag-and-drop page building: columns, free positioning, per-element styling.
- Sending newsletters, campaigns, or syncing to email services (PP-D14). Signup and CSV export only.
- A consumer recipe index, reader accounts, a marketplace, or a public developer platform.
- Any code, package, or runtime dependency on CreatorPantry. PantryPress never calls CreatorPantry.
- Self-hosting or install packages.

## Constraint

**Stack:** .NET 10, ASP.NET Core, Aspire, EF Core 10, Azure SQL (own server), Redis (own), Azure Blob
(own), YARP BFF, ASP.NET Core Identity, Angular 22 (admin SPA plus an SSR public site), Azure Front Door
Standard, Stripe, Azure Communication Services Email.

**Solution layout (PP-D01):**

```text
src/
├── PantryPress.AppHost/          # Aspire application model
├── PantryPress.ServiceDefaults/  # telemetry, health, resilience, edge hardening
├── PantryPress.Gateway/          # YARP BFF on pantrypress.app: landing, sign-in, /dashboard (PP-D19)
├── PantryPress.ApiService/       # authenticated admin API and the publishing API
├── PantryPress.Site/             # anonymous public site API (page data, reader endpoints)
├── PantryPress.Domain/           # Modules/<Context>/ plus Managers/ shared kernel
├── PantryPress.Worker/           # page-data builds, media, purges, feeds, imports, webhooks
├── PantryPress.MigrationService/ # applies migrations before dependents start
├── PantryPress.Tests/
└── web/
    ├── projects/pantrypress-ui/    # @pantrypress/ui and @pantrypress/ui/themes
    ├── projects/pantrypress-admin/ # the platform home: prerendered landing, sign-in, /dashboard (client-rendered)
    └── projects/pantrypress-site/  # public blog, Angular SSR on every request
```

**The request seam (ARCH-002).** Every API route:

```text
Controller → Facade → Business → DataLayer → Repository | Gateway
```

Controllers are thin. Facades own transactions, authorization context, and orchestration. Business holds
rules. DataLayer composes repositories. Repositories own EF Core. Gateways wrap external providers. Admin
and publishing routes live in `ApiService`; anonymous routes live in `Site`. The Node SSR server is a
presentation tier only.

**Data zones (PP-D06):** platform reference data (no `SiteId`); site-owned data (`SiteId`, private);
published public data (`SiteId`, deliberately public, removable on unpublish); reader-submitted data
(`SiteId`, personal, untrusted).

**Site isolation (PP-D03, ARCH-015).** The site is the tenant. Every site-owned entity has `SiteId`, a
global query filter, server-side stamping, and `SiteId`-leading indexes. Admin routes resolve the site from
the route plus membership. Public requests resolve it from the host the reader asked for only, through `SiteHost`:
the Host header, or, behind Front Door, the one header its rule set overwrites, believed only with the edge secret
(PP2.14, PP9.3). A site
ID is never accepted from a request body, query string, or client header. The one written exception is a link
the server sealed itself: an invitation's token names its site the way a publishing key does (tenancy.md rule
6a). Every site-scoped change owes a two-site test.

**Public tier (PP-D02, PP-D08).** Publishing builds versioned page data from published revisions behind an
atomic pointer. The SSR server makes one page-data fetch per request and never touches SQL. Output is
deterministic. Pages work without JavaScript; interactivity is `@defer (hydrate on …)` islands only. No SQL,
model call, or cross-site access is reachable from an anonymous request.

**Recipes (PP-D05).** The blogger's wording is preserved exactly. Quantities use exact rational arithmetic,
never floating point. Scaled and converted variants are precomputed when page data is built.

**Design (PP-D17, PP-D18).** Settings become an immutable theme snapshot of tokens and `data-*` attributes.
Components read only those. Colors are contrast-corrected on the server. One component tree serves every
preset.

## Restriction

- Never weaken site isolation, the Host-only rule (the reader's host, from the Host or the edge-proven header and
  nothing else), or a global query filter. `IgnoreQueryFilters()` is
  allowed only in platform-admin code with a written reason.
- Never add a dependency on an AI SDK or call a model.
- Never let reader or API input skip validation, sanitization, rate limits, or entitlements.
- Never check a plan name directly; ask the entitlements facade (ARCH-010).
- Never let a provider DTO cross a gateway boundary.
- Never render raw blogger HTML; content passes the allow-list sanitizer.
- Never add third-party script outside the PP-D11 allow-list or widen the per-site CSP by hand.
- Never re-approve golden files in bulk; update only the files a change intends.
- Never commit secrets. Local secrets use user-secrets or Aspire parameters.
- Never mark a prompt done without its stated verification.
- One seam per prompt. If a prompt needs a schema, endpoint, UI, provider, and job at once, split it.

## Usage

```bash
aspire run                                  # whole system: SQL, Redis, blob, APIs, worker, Angular apps
dotnet build PantryPress.slnx
dotnet test PantryPress.slnx                # unit, integration, two-site, golden files
dotnet format PantryPress.slnx --verify-no-changes
dotnet tool restore                         # pinned dotnet-ef
dotnet ef migrations add <Name> --project src/PantryPress.MigrationService  # applied only by MigrationService
cd src/web && npm ci
npm run lint && npm test
npm run build:site && npm run budget        # SSR build plus the RND-007 first-party budget check
```

Local hosts: the platform home (landing, sign-in, `/dashboard`) at `localhost` through the gateway, sites at
`{slug}.localhost` (SITE-009, PP-D19; the code moves from `app.localhost` in PPL.1).

Skills (`.claude/skills/`): `add-endpoint`, `add-site-entity`, `add-public-endpoint`,
`add-recipe-feature`, `add-editor-block`, `add-media-feature`, `add-background-job`,
`add-external-integration`, `add-theme-component`, `add-render-artifact`, `add-admin-component`,
`add-aspire-resource`.

Reviewers (`.claude/agents/`, read-only): `architecture-reviewer`, `site-isolation-auditor`,
`api-contract-checker`, `public-site-reviewer`, `design-review`, `integration-reviewer`,
`test-gap-analyzer`. Run `public-site-reviewer` after any change to public output and
`site-isolation-auditor` after any change to site-owned data.

## Behavior

1. **Read first.** Read the prompt, the requirement IDs it cites, and the matching rule files.
2. **Plan.** List the files you will touch, the seam, the tests, and anything you will not do. When the
   prompt says so, wait for approval.
3. **Implement one seam** with the matching skill.
4. **Test.** Focused tests first, then the affected suites. Add a two-site test for site-owned, public, or
   reader data.
5. **Review.** Run the named reviewers and resolve or record findings.
6. **Report** the files changed, commands run, results, and remaining limitations. Mark a prompt `- done`
   only after verification passes, and add a delivery note if what shipped differs from what was asked.

When a requirement is marked **DECIDE**, use the stated default and say so. Do not resolve it silently.
User-facing copy uses plain language: no DNS, caches, blobs, page data, or HTML unless the blogger opted
into a custom domain.

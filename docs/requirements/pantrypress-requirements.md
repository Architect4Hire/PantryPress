# PantryPress — Requirements

Business, architectural, technical, and operational requirements for **PantryPress**, a standalone, hosted
food-blogging platform: a "WordPress for food bloggers" with everything built in. Bloggers write posts and
recipes in PantryPress and publish them to a fast, SEO-ready site on an automatic `{name}.pantrypress.app`
subdomain. CreatorPantry users can also publish into PantryPress, but nobody needs CreatorPantry to use it.

This document is the functional source of truth for the PantryPress repository. It is written to be broken
into milestones and atomic SCRUB prompts (Scope, Constraint, Restriction, Utilization, Behavior) in the same
style as the CreatorPantry library. Suggested home: `docs/requirements/pantrypress-requirements.md`, with
prompts in `docs/prompts/pantrypress-scrub-prompts.md`.

*Status: draft v2.1, 2026-10-22. Reworked for a standalone product in its own repository. v2.1 adopts
"Studio" site customization (PP-D17), replacing the earlier "light customization only" rule. Every item marked
**DECIDE** is an open choice with a stated default; later prompts assume the default until PP0.1 records a
different answer.*

Companion spec: `docs/requirements/pantrypress-customization-spec.md` defines every design option, value, default, token, contrast
rule, preset, and acceptance test referenced by THM-001 to THM-013, and the section library, page blocks, and
page templates referenced by SITE-011, SITE-012, and POST-011.

---

## 1. Product summary

### 1.1 One sentence

PantryPress gives a food blogger a beautiful, fast, search-friendly blog with a real recipe card, ratings,
comments, a subscriber list, ads, and analytics, without plugins, theme hunting, updates, or servers.

### 1.2 What the owner has decided

| Topic | Decision |
| --- | --- |
| Product independence | PantryPress is a standalone product. Using it does not require CreatorPantry. CreatorPantry is an optional source that publishes into PantryPress through PantryPress's publishing API. |
| Repository | A new PantryPress repository with its own solution, Aspire AppHost, domain, toolkit, and CI. No shared code with CreatorPantry. |
| Azure | Shared with CreatorPantry only where it is cheap and safe (Front Door profile, Container Apps environment, monitoring workspace). PantryPress has its **own SQL server, Redis, storage, and Key Vault**. |
| Stack | ASP.NET Core and Aspire on the back end; Angular 22 on the front end, with SSR for public blog pages. |
| Public rendering | Angular SSR on every request, cached at Azure Front Door. |
| Edge | Azure Front Door **Standard**. |
| Hosting model | Multi-tenant SaaS on Azure. Every site gets an automatic `{name}.pantrypress.app` subdomain. |
| Custom domains | Opt-in for paid plans. Everyone else stays on the subdomain. |
| Newsletters | Signup forms and subscriber CSV export only. Configuring and sending newsletters is out of scope. No sync to email services. |
| Site design | Six design presets as starting points, plus a deep customizer of typed settings (PP-D17): any brand color with enforced contrast, curated fonts and type scale, shapes, textures, header and hero styles, sidebar position, recipe card styles, section order, and personal text. No custom CSS, HTML, templates, scripts, or font uploads. |
| Design system | A new PantryPress design system. Nothing is shared with `@creator-pantry/ui`. |
| Audience | Hobby bloggers bordering on prosumer. |
| Revenue | Subscription tiers. |
| Extensibility | Closed set. Everything a food blogger needs is built in. No plugins, no third-party themes, no custom code. Design freedom comes from typed settings, not code. |
| Team and schedule | One developer using Claude Code. Open timeframe; phases are ordered by value and risk. |

### 1.3 What PantryPress is not

- **Not a general-purpose CMS.** No page builder, plugin API, theme upload, or tenant-authored code.
- **Not an AI writing tool.** PantryPress has no AI features. AI-assisted recipe development and writing is
  CreatorPantry's job, and CreatorPantry can publish into PantryPress.
- **Not a consumer recipe index.** No cross-site discovery hub, reader feed, or marketplace. Each site
  stands alone.
- **Not a newsletter tool.** It collects subscribers and exports a CSV; sending stays with the creator's
  own email tool.
- **Not self-hosted.** There is no install package.
- **Not a public developer platform.** The publishing API exists for CreatorPantry and similar trusted
  authoring tools, with per-site keys; there is no app marketplace.

---

## 2. Business requirements

### 2.1 Personas

| Persona | Description | What they need from PantryPress |
| --- | --- | --- |
| **Hobby blogger** | Cooks, photographs, writes on evenings and weekends. Low technical skill. May have tried WordPress and given up on plugins and updates. | An easy editor with a real recipe card, a site that looks good on day one, posts that "just work" on Google and Pinterest, and no maintenance. |
| **Prosumer blogger** | Growing audience (roughly 5k–100k monthly page views), starting to earn from ads and affiliate links. | Speed, SEO, a growing subscriber list, ads and affiliate support, an optional custom domain, analytics, and a painless move from WordPress. |
| **CreatorPantry user** | Develops recipes and drafts posts with AI in CreatorPantry. | One connection that publishes CreatorPantry content to their PantryPress blog, then edit or publish there. |
| **Reader** (anonymous public) | Arrives from Google, Pinterest, or social media, usually on a phone, often mid-cook. | Fast page, jump-to-recipe, readable recipe card, print view, scaling and unit toggle, ratings and comments. |
| **Platform operator** | The developer, acting as support and admin. | Visibility into every site, abuse takedown, plan overrides, and incident tools a team of one can run. |

### 2.2 Business requirements

| ID | Requirement |
| --- | --- |
| BIZ-001 | A blogger can write a post with an embedded recipe in PantryPress and publish it live in one confirmed action, with no knowledge of HTML, hosting, DNS, or SEO markup. |
| BIZ-002 | A new blogger can launch a working, good-looking blog on an automatic subdomain within 10 minutes of sign-up, inside a guided "Launch my blog" flow. |
| BIZ-003 | PantryPress provides, as built-in features, everything on the food-blogger checklist in section 2.4. Nothing on it requires a plugin, a third-party account wired up by hand, or code. |
| BIZ-004 | Revenue comes from recurring subscription tiers (section 2.3). Entitlements are enforced by the platform. |
| BIZ-005 | Every published page meets food-blog search requirements out of the box: schema.org Recipe structured data, Pinterest-ready images and metadata, sitemap, RSS, and strong Core Web Vitals. |
| BIZ-006 | The blogger always owns their content. Content can be exported at any time (HTML, Markdown, recipe JSON, images, comments, ratings, and subscriber CSV), including after cancellation for a stated grace period. |
| BIZ-007 | Prosumer features (ads, affiliate links, optional custom domain, analytics) let a blogger earn enough that a paid plan pays for itself. |
| BIZ-008 | The platform is operable by one person: routine operations are automated, incidents are rare by design (cache-first delivery), and abuse can be handled from an admin console in minutes. |
| BIZ-009 | Free subdomains must not become a vector for spam, phishing, or malware. Abuse controls ship before public sign-up opens. |
| BIZ-010 | PantryPress works fully without CreatorPantry. A CreatorPantry user can connect the two with a per-site publishing key and no other shared account. |
| BIZ-011 | A WordPress food blogger can move their posts, recipes (WP Recipe Maker and Tasty Recipes), images, comments, and permalinks into PantryPress. |

### 2.3 Subscription tiers (DECIDE prices and limits)

A starting proposal. PP-D09 records the final numbers. Limits live in configuration, not code.

| Capability | **Starter** | **Plus** | **Pro** |
| --- | --- | --- | --- |
| Sites | 1 | 1 | 3 |
| Address | `{name}.pantrypress.app` | Subdomain, plus an optional custom domain | Subdomain, plus optional custom domains (one per site) |
| "Made with PantryPress" footer | Required | Removable | Removable |
| Site design (PP-D17) | 2 presets (Market, Hearth), mode, logo, tagline, brand and highlight color | All presets and Studio options except textures and the Banner and Masthead headers (DECIDE) | All presets and all Studio options |
| Editor, recipe card, jump-to-recipe, print, scaling, units | Yes | Yes | Yes |
| Site members (co-authors) | 1 | 3 | 10 |
| Ratings and comments | Yes | Yes | Yes |
| Newsletter signup forms and subscriber CSV export | Up to 500 subscribers | Up to 5,000 | Up to 25,000 |
| Ads (AdSense) and affiliate links | No | Yes | Yes |
| Ad-network readiness (ads.txt, network slot layout) | No | No | Yes |
| Analytics | 30 days, page views | 12 months, full dashboard | 24 months, full dashboard, CSV export |
| Media storage | 2 GB | 20 GB | 100 GB |
| Publishing API (CreatorPantry connection) | Yes | Yes | Yes |
| WordPress import | No | One-time | Unlimited |
| Support | Help center | Email | Priority email |

**DECIDE (PP-D10):** free Starter forever, or a 14-day trial then paid. *Default: 14-day trial, no
free-forever tier*, because a free subdomain tier is the main abuse and cost risk for a team of one.

### 2.4 The food-blogger checklist (closed feature set)

A feature not on this list is out of scope until the list is amended.

**Writing and recipes**
- Block editor for posts: paragraphs with basic formatting, headings, images, galleries, lists, quotes,
  tip and note callouts, allow-listed video embeds, and a recipe card block.
- Structured recipe editor: title, summary, yield, prep, cook, and total times, ingredient groups with
  quantities and units, step groups with optional step images, equipment, notes, course, cuisine, diet,
  keywords, and optional creator-entered nutrition per serving.
- Paste-to-parse for ingredient lists, keeping the blogger's original wording.
- Autosave, revision history with restore, preview, publish now, schedule, and unpublish.
- Media library with upload, alt text, focal-point cropping, and usage tracking.
- Categories (course, cuisine, diet, season) and tags.
- Site pages: About, Start here, Contact, Privacy, Disclosure, Accessibility, Terms, and custom pages, built
  from blocks, page blocks, and shared homepage sections, with starter templates. No drag-and-drop page builder
  (PP-D18).

**Site design**
- Six design presets (Market, Hearth, Larder, Farmhouse, Sunny, Supper Club; names DECIDE) with light, dark,
  and auto modes.
- Customizer with live preview, apply, discard, and revert: brand name, tagline, monogram and mark shape; any
  brand and highlight color with automatic contrast correction; background tone and texture; curated heading
  and body fonts, body size, heading scale, and drop cap; corner and photo radius; button style.
- Five header styles and an announcement bar; four hero image styles; sidebar right, left, or none; text
  column width.
- Three recipe card styles, three step-number styles, recipe badges, a "From my kitchen" note, and a custom
  jump-button label.
- Section toggles and ordering after the recipe card; handwritten sign-off; newsletter heading and button
  text.

**Reader experience**
- Recipe card with jump-to-recipe button, print view (with and without images), servings scaling
  (½×, 1×, 2×, 3×), US/metric toggle, and "cook mode" that keeps the screen awake. Scaled and converted
  values are **precomputed on the server**; the browser only switches between them.

**Discovery and SEO**
- schema.org `Recipe`, `Article`, `BreadcrumbList`, `Person`, and `WebSite` JSON-LD.
- Canonical URLs, Open Graph, Twitter/X cards, Pinterest rich pins, and a pinnable vertical image.
- XML sitemap, image sitemap, RSS feed per site and per category, and `robots.txt`.
- Stable permalinks and a 301 redirect manager.
- On-site search.
- Google Search Console and Pinterest verification fields.

**Audience**
- Star ratings and reviews, with `AggregateRating` only for real ratings.
- Threaded comments with a moderation queue.
- Newsletter signup forms (inline, end-of-post, footer) with double opt-in and subscriber CSV export.
- Contact form delivered to the blogger's email.
- Social links and share buttons (no third-party share scripts).

**Earning**
- Google AdSense placement for Plus and Pro.
- Ad-network readiness for Pro: editable `ads.txt`, standard slot layout, allow-listed networks (PP-D11).
- Affiliate links with automatic `rel="sponsored nofollow"` and an auto-inserted disclosure.

**Insight**
- Privacy-friendly page-view analytics with no cookies: top posts, referrers, and search terms.
- Optional Google Analytics 4, loaded only after consent.

**Trust and compliance**
- Consent banner when ads or GA4 are on.
- Accessible design options meeting WCAG 2.2 AA in every combination.
- Privacy policy and affiliate disclosure templates the blogger reviews.

### 2.5 Success measures (DECIDE targets)

| ID | Measure | Target |
| --- | --- | --- |
| KPI-001 | Sign-up to first published post | Median under 30 minutes |
| KPI-002 | Trial-to-paid conversion | 15% or more |
| KPI-003 | Monthly logo churn on paid plans | Under 4% |
| KPI-004 | Public pages passing Core Web Vitals (CrUX "good") | 90% or more of URLs |
| KPI-005 | Recipe posts with valid Recipe rich-result markup | 100% |
| KPI-006 | Infrastructure cost per paying site | Under 15% of average revenue per site |
| KPI-007 | Operator hours per week on support and abuse | Under 4 |

---

## 3. Architectural requirements

### 3.1 Decisions (PP-D01 to PP-D18)

**None is confirmed until PP0.1.** Items marked "owner" were decided on 2026-10-22.

**PP-D01 Own repository, own Aspire application (owner).** PantryPress lives in a new repository with its
own .NET solution and Aspire AppHost. It shares **no code** with CreatorPantry. It reuses CreatorPantry's
proven *patterns* (module-first domain, the request seam, BFF authentication, tenancy filters, outbox,
SCRUB delivery), written fresh for PantryPress. Projects:

```text
src/
├── PantryPress.AppHost/          # Aspire application model
├── PantryPress.ServiceDefaults/  # telemetry, health, resilience
├── PantryPress.Gateway/          # YARP BFF on pantrypress.app: landing, sign-in, /dashboard (PP-D19)
├── PantryPress.ApiService/       # authenticated admin API and the publishing API
├── PantryPress.Site/             # anonymous public site API (page data, comments, ratings, signups …)
├── PantryPress.Domain/           # module-first: Modules/<Context>/ plus Managers/ shared kernel
├── PantryPress.Worker/           # page-data builds, media, purges, feeds, imports, webhooks
├── PantryPress.MigrationService/ # applies migrations before dependents start
├── PantryPress.Tests/
└── web/                          # Angular 22 workspace
    ├── projects/pantrypress-ui/    # @pantrypress/ui design system
    ├── projects/pantrypress-admin/ # admin app (the blogger's dashboard and editor)
    └── projects/pantrypress-site/  # public blog app, Angular SSR (Node server "pantrypress-site")
```

**PP-D02 Cache-first delivery with Angular SSR on every request (owner).** Public pages are rendered per
request by the `pantrypress-site` Node server and cached at Front Door, so most reader traffic is served
from the edge. Publishing builds **page data**: versioned JSON view models per route, derived from published
post snapshots and site settings, written to blob storage behind an atomic pointer and warmed into Redis.
The SSR server reads page data through `PantryPress.Site` and never touches SQL. Dynamic features are small
JSON endpoints and hydrated islands.
- **Caching:** long shared TTL at the edge, purged explicitly on publish, settings, theme, and moderation
  changes.
- **Degraded mode:** SQL, ApiService, or Worker outages do not affect public pages. If the SSR server or site
  API is down, the edge keeps serving cached pages. **DECIDE at PP9.4:** whether Front Door can serve stale
  content on origin errors; if not, rely on long TTLs plus purge.

**PP-D03 Site is the tenant.** An Identity **account** can own or belong to several **sites** (up to its
plan limit). Each site has members with roles `Owner`, `Editor`, and `Author`. `PlatformAdmin` is the only
platform-wide role. All site-owned data carries `SiteId` with a global query filter and server-side
stamping. Admin routes resolve the site from the route plus membership; public requests resolve it **from
the host the reader asked for only** (the Host header, or behind Front Door the edge-proven
`X-PP-Reader-Host`; PP2.14), through `SiteHost`. A site ID is never accepted from a request body, query string,
or client header.

**PP-D04 Publishing is a snapshot.** Publishing a post creates an immutable **published revision**: the
post's blocks, the referenced recipes' published revisions, media references, SEO fields, and taxonomy at
that moment. Page data is built only from published revisions and site settings. Editing a draft changes
nothing public until the blogger publishes again.

**PP-D05 Recipes are structured and calculations are deterministic.** Recipes are structured data, not
rich text. Scaling, unit conversion, temperature conversion, time totals, and nutrition-per-serving
arithmetic are deterministic domain code, written in PantryPress. The blogger's own ingredient and step
wording is preserved exactly; parsing enriches it and never rewrites it. Scaled and converted variants are
precomputed when page data is built.

**PP-D06 Data zones.**
- **Platform reference data** (no `SiteId`): units, conversions, a small ingredient density table, diet and
  allergen vocabulary, cuisines, courses.
- **Site-owned data** (`SiteId`, private): posts, recipes, revisions, media, settings, pages, members.
- **Published public data** (`SiteId`, deliberately public): published revisions' page data and public media
  derivatives. Removable on unpublish.
- **Reader-submitted data** (`SiteId`, personal data, untrusted): comments, ratings, subscribers, contact
  messages, and consent records.

**PP-D07 A new design system, `@pantrypress/ui` (owner).** One Angular library with a shared token
foundation and two entry points:
- `@pantrypress/ui` for the admin app: forms, editor chrome, tables, dialogs, and dashboards.
- `@pantrypress/ui/themes` for public blog themes: SSR-safe reader components, theme token sets, and
  strict performance budgets.
Nothing depends on `@creator-pantry/ui`. **DECIDE:** one library with two entry points (default) or two
libraries.

**PP-D08 Public themes are an Angular 22 SSR application.** `projects/pantrypress-site` is served by the
`pantrypress-site` Node server (Angular's Node app engine), added to the AppHost as a JavaScript app and
exposed only through Front Door.
- **Host only:** the SSR server forwards the host the reader asked for to `PantryPress.Site`; Node never decides
  tenancy. Behind Front Door that host arrives in a header Front Door overwrites and proves with the edge secret
  (PP2.14).
- **One fetch per page:** a route resolver fetches that route's page data (Redis, then blob, plus an
  in-process LRU). No other data access during SSR. Output is deterministic for given page data.
- **Islands only:** scaling and units, cook mode, ratings, comments, signup, and search use incremental
  hydration (`@defer` with `hydrate on` triggers). The rest stays server HTML. Pages work without
  JavaScript.
- **Budget check first:** PP7.2 measures the Angular runtime against RND-007. If it cannot fit, hydration is
  disabled on public pages and islands become tiny vanilla scripts (**DECIDE** at PP7.2).
- **Admin app:** client-rendered Angular SPA behind the BFF. *Owner to confirm*; SSR adds little behind a
  sign-in.

**PP-D09 Subscriptions via Stripe, entitlements in-house.** Stripe owns prices, checkout, invoices, tax,
the customer portal, and dunning. PantryPress owns an entitlements read model derived from Stripe webhooks;
every gated feature asks the entitlements facade. The subscription belongs to the **account** that pays.

**PP-D10 Trial instead of free-forever** (section 2.3).

**PP-D11 Third-party scripts are an allow-list.** Only AdSense, named ad networks, GA4, Pinterest
verification, and the spam challenge can add script to a page, each through a typed setting. Content
Security Policy is generated per site from its enabled integrations.

**PP-D12 No AI anywhere in PantryPress.** Rendering, editing, parsing, and moderation are deterministic.
AI-assisted creation is CreatorPantry's job.

**PP-D13 Domains, cookies, and the edge (owner: Front Door Standard).**
- `*.pantrypress.app` is a wildcard DNS record pointing at Front Door Standard with a wildcard certificate.
  Every site always has its subdomain. The platform home - the landing page, sign-in and sign-up, and the
  dashboard under `/dashboard` - lives on the apex, `pantrypress.app`; `app.` and `www.` redirect to it
  (PP-D19, ADR 0019, which supersedes the earlier `app.pantrypress.app`).
- **Custom domains are opt-in** for paid plans, using Front Door custom domains with managed certificates,
  verified by TXT record. The subdomain redirects to the custom domain while it is attached.
- The BFF session cookie is `__Host-`, host-only, `Secure`, `HttpOnly`, and `SameSite=Lax` or stricter,
  because tenant sites run third-party ad scripts on sibling subdomains. Apply to add `pantrypress.app` to
  the Public Suffix List.
- **Standard, not Premium:** no managed WAF rule sets, bot rule set, or Private Link. Compensate with WAF
  custom rules (rate limits, geo and IP blocks), origins locked to the `AzureFrontDoor.Backend` service tag
  plus an edge secret Front Door's rule set adds (PP2.14; `X-Azure-FDID` alone proves nothing on a shared profile),
  and application-level defenses. **Upgrade trigger (DECIDE):** a sustained
  attack the custom rules cannot handle, or revenue that makes the Premium base fee small.

**PP-D14 Newsletter signup and CSV export only (owner).** PantryPress collects subscribers with double
opt-in, stores consent, and exports CSV. No composing, campaigns, ESP setup, sync, or sending. Platform email
is transactional only: signup confirmations, contact form messages, comment notices, account and billing
mail.

**PP-D15 The publishing API is how other tools publish into PantryPress.** A versioned REST API
(`/api/publishing/v1`) on `PantryPress.ApiService`, authenticated with **per-site publishing keys** created
by a site Owner (scoped, revocable, hashed at rest). It accepts the documented **PantryPress Publish
Format** (a post with blocks, recipes, media references, SEO fields, and taxonomy), creates or updates a
draft, and optionally publishes it. Idempotency keys make retries safe. Media is uploaded through the API or
fetched from HTTPS URLs with SSRF protection. CreatorPantry implements its side as a publishing adapter in its
own repository (see section 7.3). PantryPress never calls CreatorPantry.

**PP-D16 Shared Azure, separate data (owner).** PantryPress has its own Azure SQL server, Redis, storage
account, Key Vault, Communication Services, and DNS zone. It shares only:
- the Front Door Standard **profile** (separate endpoints, routes, and WAF policies), so the base fee is
  paid once;
- the Container Apps **environment** (separate apps and managed identities);
- the Log Analytics and Application Insights **workspace** (separate cloud role names).
**Decided (2026-10-24):** the PantryPress repository owns the shared resources, in the Terraform `platform`
stack (`docs/infrastructure/terraform/platform/`), one set per environment; CreatorPantry's IaC references them
as existing resources.

**PP-D17 Studio site customization (owner, 2026-10-22).** Bloggers get far more design control than "canned
themes with light customization", while the platform stays closed. Every option is a typed, validated setting
stored in a versioned `SiteThemeSettings` document. On apply, the server builds an immutable **theme snapshot**:
light and dark CSS custom properties, `data-*` layout attributes, font selections, sanitized text, section order,
and a contrast report. SSR writes the snapshot onto the site root; components style themselves only through
those tokens and attributes, and public pages run no customization JavaScript. Brand colors are free choice, but
derived text and fill colors are corrected on the server to WCAG AA, and unfixable colors are refused. Fonts come
from a curated self-hosted registry. The six launch looks are presets over the same option set rather than
separate theme codebases, so one component tree serves every site. Still excluded: custom CSS, HTML, templates,
scripts, font uploads, and third-party themes. Details: the customization spec.

**PP-D18 Section-based pages, no page builder (owner, 2026-10-22).** PantryPress has no free-form,
drag-and-drop page builder: no columns, canvases, absolute positioning, or per-element styling. Instead, the
homepage and site pages share one **section library** of typed, data-aware sections (featured recipe, latest
posts, category row, recipe grid, about strip, newsletter signup, start-here links). The homepage is an ordered
list of sections. Site pages are block documents that can include those sections as blocks, plus a few
page-only blocks (intro with photo, FAQ, social links, contact form). New sites get page templates (About,
Start here, Contact) so nobody starts from a blank page. Every section and block inherits the site's design
settings (PP-D17), so accessibility and the performance budget hold on every page.

### 3.2 Logical architecture

```text
              Readers (anonymous)                                   Bloggers
                     │                                                  │
     {name}.pantrypress.app / custom domain                    pantrypress.app (PP-D19)
                     │                                                  │
     ┌───────────────▼─────────────── Front Door Standard (shared profile) ──────────────▼──────────┐
     │  wildcard TLS · cache · WAF custom rules · purge                                             │
     └──────┬──────────────────────┬───────────────────────────────────────────────┬────────────────┘
   page routes (cache miss)   /api/public/*, sitemaps, feeds                        │
            │                      │                                                │
  ┌─────────▼────────┐   ┌─────────▼────────────┐                    ┌─────────────▼────────────┐
  │ pantrypress-site │──►│  PantryPress.Site    │                    │ PantryPress.Gateway (BFF)│
  │ Node, Angular 22 │   │  public site API     │                    │ + pantrypress-admin SPA  │
  │ SSR per request  │   │  Host → SiteHost     │                    └─────────────┬────────────┘
  └──────────────────┘   │  page data, reader   │                                  │
                         │  endpoints           │                    ┌─────────────▼────────────┐
                         └───┬──────────┬───────┘                    │ PantryPress.ApiService   │
                             │          │                            │ admin API + publishing   │◄── CreatorPantry
                    Redis (own)         │ dynamic writes             │ API (per-site keys)      │    adapter (optional)
                             ▲          ▼                            └─────────────┬────────────┘
                             │   PantryPress.Domain ◄──────────────────────────────┘
                             │          │
                Blob (own): page data,  ▼
                public media      Azure SQL (own server)
                             ▲          ▲
                             └── PantryPress.Worker: page-data builds, media derivatives,
                                 purges, sitemaps, feeds, search index, imports, Stripe webhooks
```

### 3.3 Architectural requirements

| ID | Requirement |
| --- | --- |
| ARCH-001 | PantryPress is a standalone repository and Aspire application with no code or runtime dependency on CreatorPantry. The domain is module-first (`Modules/<Context>/`) with boundary tests. |
| ARCH-002 | Every API route follows `Controller → Facade → Business → DataLayer → Repository \| Gateway`. Admin and publishing routes live in `PantryPress.ApiService`; anonymous routes live in `PantryPress.Site`. The Node SSR server is a presentation tier only. |
| ARCH-003 | The public tier resolves the tenant only from the host the reader requested, via `SiteHost`: the request Host, or, behind Front Door, the header its rule set overwrites with that host, believed only with the edge secret (PP2.14). The SSR server forwards that host and never chooses a site. Unknown hosts get a neutral 404. |
| ARCH-004 | Page data is built only from immutable published revisions and site settings, never from drafts or live editing tables. |
| ARCH-005 | Rendering is deterministic: the same page data and SSR build produce byte-identical HTML. Golden-file tests prove it. SSR components read only their page data. |
| ARCH-006 | A publish writes page data for a version under a new versioned blob prefix, switches one pointer atomically, and purges the edge. Rollback is a pointer switch. |
| ARCH-007 | Public pages keep rendering when SQL, ApiService, or the Worker is down. When the SSR server or site API is down, the edge keeps serving cached pages. Only islands degrade. |
| ARCH-008 | Public pages are complete and readable with JavaScript disabled. |
| ARCH-009 | The publishing API is versioned, key-authenticated per site, idempotent, rate-limited, documented with OpenAPI, and goes through the same facades as the admin editor. It never bypasses validation, sanitization, or entitlements. |
| ARCH-010 | Entitlements are read through one facade. No feature checks a plan name directly. |
| ARCH-011 | External providers (Stripe, Front Door, email, malware scanning, Safe Browsing, spam challenge, ad networks) sit behind typed gateways with resilience, idempotency where available, and provider IDs only in integration records. |
| ARCH-012 | Theme components, tokens, and CSS are versioned with the SSR build. Theme or settings changes take effect on the next render; the platform purges affected sites in throttled batches. |
| ARCH-013 | No SQL query, model call, or cross-site access is reachable from an anonymous page request, including inside SSR. |
| ARCH-014 | Search index, sitemap, feeds, and category and archive page data are derived artifacts rebuilt by jobs after a publish. |
| ARCH-015 | Site isolation: every site-owned entity has `SiteId`, a global query filter, server-side stamping, and `SiteId`-leading indexes. Every site-scoped feature has a two-site test. |

---

## 4. Technical requirements

### 4.1 Accounts and identity (ACCT)

| ID | Requirement |
| --- | --- |
| ACCT-001 | ASP.NET Core Identity accounts with email and password, verified email before publishing, password reset, and optional TOTP two-factor. |
| ACCT-002 | The admin SPA talks only to `PantryPress.Gateway` (YARP BFF) with secure `HttpOnly` cookies. The browser never stores tokens. Antiforgery protects state-changing requests. |
| ACCT-003 | Sign-up applies abuse controls: rate limits, disposable-email checks (DECIDE), and the spam challenge. |
| ACCT-004 | Account deletion and data export are self-service, with a grace period. |
| ACCT-005 | `PlatformAdmin` is assigned only by the operator and is audited. |

### 4.2 Sites and tenancy (SITE)

| ID | Requirement |
| --- | --- |
| SITE-001 | A `Site` holds name, tagline, slug, status (`Provisioning`, `Live`, `Suspended`, `ReadOnly`, `Offline`, `Deleted`), design settings and current theme snapshot, settings, time zone, and locale. |
| SITE-002 | Slugs are 3–40 lower-case letters, digits, and hyphens, unique platform-wide, and checked against a reserved list. Availability never reveals who holds a taken slug. |
| SITE-003 | Creating a site allocates `{slug}.pantrypress.app` automatically, reachable over HTTPS within 60 seconds. |
| SITE-004 | A slug can be renamed at most N times a year (DECIDE). The old host 301-redirects for 12 months and is not reissued meanwhile. |
| SITE-005 | `SiteHost` maps every host (subdomain, custom domain, `www` variant) to one site, with one canonical host. Non-canonical hosts 301 to it. |
| SITE-006 | Site settings: identity (name, logo, favicon, tagline), author profiles, social links, navigation, homepage layout, design settings (THM-002), integrations, legal pages, and comment and rating policies. |
| SITE-007 | Site pages (About, Start here, Contact, Privacy, Disclosure, Accessibility, Terms, and up to 10 custom pages) use the block editor with the post blocks except the recipe card, plus section blocks (SITE-011) and page blocks (POST-011). Privacy, Disclosure, Accessibility, and Terms start from legal templates. |
| SITE-008 | Suspension replaces public output with a neutral notice without deleting content. Restoring is immediate. |
| SITE-009 | Local development resolves `{slug}.localhost` through Aspire, so subdomain routing is testable without DNS. |
| SITE-010 | Site membership: Owner (everything, including billing and keys), Editor (all content and moderation), Author (own posts only; cannot publish if the Owner requires review). Invitations by email. |
| SITE-011 | A section library shared by the homepage and site pages (PP-D18): featured recipe, latest posts, category row, recipe grid, about strip, newsletter signup, and start-here links. Each section has typed options only (heading, intro text, source, count, layout); limits are in the customization spec. Data-backed sections are rebuilt with page data after a publish, never queried per request. |
| SITE-012 | Page templates for About, Start here, and Contact, prefilled from site settings with clearly marked placeholders. "Launch my blog" creates About and Start here as drafts. Templates are starting content, not locked layouts. |

### 4.3 Recipes and calculations (RCP)

| ID | Requirement |
| --- | --- |
| RCP-001 | A recipe holds title, summary, yield (amount and unit), prep, cook, additional, and total times, ingredient groups, step groups with optional images, equipment, notes, course, cuisine, diet, keywords, optional video, and optional nutrition per serving. |
| RCP-002 | Each ingredient line stores the blogger's original text plus parsed quantity (including fractions and ranges), unit, item, and note. Parsing never rewrites the original text; unparseable lines are kept and flagged. |
| RCP-003 | Paste-to-parse turns a pasted ingredient list or step list into structured lines for review before saving. |
| RCP-004 | Deterministic scaling (½×, 1×, 2×, 3× and custom servings) with sensible rounding and fraction display. Unscalable lines are shown unscaled and flagged. |
| RCP-005 | Deterministic US and metric conversion for volume, weight, and temperature, with a small density table for common ingredients; conversion across volume and weight happens only when a density is known. |
| RCP-006 | Nutrition is creator-entered only, shown per serving with a disclaimer. PantryPress never estimates nutrition, and dietary or allergen labels appear only when the blogger sets them. |
| RCP-007 | Recipe revisions: published recipe revisions are immutable; editing creates a draft. |
| RCP-008 | A deterministic schema.org `Recipe` JSON-LD generator with a missing-required-fields report. |
| RCP-009 | A recipe can appear in more than one post; the post's recipe card block references a recipe, not a copy. |
| RCP-010 | A recipe can carry up to 4 badges from a closed list (Kitchen-tested, Freezer-friendly, One bowl, Kid favorite, Make-ahead, Gluten-free, Dairy-free, Vegan, Under 30 minutes, Budget) and an optional "From my kitchen" note of up to 280 characters. Diet badges affect JSON-LD only when the recipe's diet field agrees. |

### 4.4 Posts, editor, and publishing (POST)

| ID | Requirement |
| --- | --- |
| POST-001 | Posts are stored as a versioned block document (DECIDE editor library; default TipTap/ProseMirror via an Angular wrapper) with an allow-listed block and mark schema. |
| POST-002 | Blocks: paragraph (bold, italic, link), heading, image, gallery, list, quote, tip and note callouts, allow-listed video embed, recipe card (reference), and divider. |
| POST-003 | Autosave drafts, revision history with compare and restore, and an "unsaved changes" guard. |
| POST-004 | Post fields: title, slug, excerpt, featured image, pinnable image, categories, tags, author, SEO title, meta description, and canonical override. |
| POST-005 | Publish now, schedule (site time zone), unpublish, and republish. Publishing creates an immutable published revision (PP-D04). |
| POST-006 | A publish readiness check lists blocking and warning items in plain language: missing featured image, missing alt text, missing recipe JSON-LD fields, missing meta description, slug conflict. |
| POST-007 | Stable URLs `/{post-slug}/`; a slug change creates a 301 automatically. "Updated on" and `dateModified` follow material republishes. |
| POST-008 | Preview renders the draft with the site's theme behind a signed, expiring preview token. |
| POST-009 | Publish-to-live (page-data build, upload, pointer switch, purge) completes in under 60 seconds at p95. |
| POST-010 | Optional review workflow: an Owner can require that Authors submit for review instead of publishing. |
| POST-011 | Page-only blocks for site pages: intro with photo (image, heading, text, optional button), FAQ (up to 20 question and answer pairs), social links (from site settings), and contact form (at most one per page, ENG-007). FAQ emits `FAQPage` JSON-LD only when the blogger turns it on (DECIDE default off). |

### 4.5 Media library (MED)

| ID | Requirement |
| --- | --- |
| MED-001 | Upload images (JPEG, PNG, WebP, HEIC converted) through the BFF to staging, with size limits, type sniffing, and malware scanning before acceptance. |
| MED-002 | Originals are private and immutable. Public derivatives (AVIF and WebP at responsive widths, JPEG fallback, 1000×1500 pinnable image) are generated on demand by the Worker. EXIF location data is stripped. |
| MED-003 | Alt text, caption, credit, and focal point per image. Missing alt text is a readiness warning. |
| MED-004 | Usage tracking shows where each image is used. Deleting an in-use image is blocked; unused public derivatives are cleaned up after a grace period. |
| MED-005 | Storage counts against the plan quota. |

### 4.6 Rendering and themes (RND, THM)

| ID | Requirement |
| --- | --- |
| RND-001 | The SSR site renders post, recipe print, home, category, tag, archive, page, search, 404, and error pages from page data per request. |
| RND-002 | Block content is converted to HTML on the server through an allow-list sanitizer when page data is built. Text is encoded; no inline handlers or `javascript:` URLs survive. |
| RND-003 | The recipe card renders precomputed scaling and unit variants; the page does no quantity arithmetic. |
| RND-004 | Nutrition and dietary labels render only as entered, with the disclaimer (RCP-006). |
| RND-005 | Page JSON-LD is the RCP-008 output plus Article, BreadcrumbList, Person, WebSite, and `AggregateRating` from real ratings only. It passes Rich Results validation in tests. |
| RND-006 | Images use `srcset`/`sizes`, width and height, lazy loading below the fold, and `fetchpriority="high"` on the LCP image. |
| RND-007 | Performance budget per page: under 100 KB compressed first-party CSS and JavaScript, including the Angular runtime and hydration code, excluding ads. CI fails on a breach. |
| RND-008 | The print view prints only the recipe card on one or two pages, with an image toggle and the site URL. |
| THM-001 | Six design presets at launch (Market, Hearth, Larder, Farmhouse, Sunny, Supper Club; DECIDE final names and looks), each a complete set of option values over one shared component tree, each with light, dark, and auto modes. |
| THM-002 | Customization through typed settings only (PP-D17): presets; site name, tagline, monogram, mark shape, logo, favicon; brand and highlight color; background tone and texture; curated heading and body fonts, body size, heading scale, drop cap; corner and photo radius; button style; header style and announcement bar; hero style, sidebar position, text width; recipe card and step style; section toggles and order; sign-off and newsletter text. No custom CSS, HTML, templates, scripts, or font uploads. Allowed values, defaults, and limits are in the customization spec. |
| THM-003 | Every reachable combination of options meets WCAG 2.2 AA in automated checks, and every preset passes a manual keyboard and screen-reader check, in light and dark modes. |
| THM-004 | Design changes are drafts previewed against the blogger's own latest post. Apply writes a new immutable theme snapshot and purges the site's edge cache; Discard drops the draft; the last 20 snapshots can be restored. |
| THM-005 | Color engine: derived tokens (dark accent, readable link color, ink on accent and highlight) are computed on the server with WCAG 2.x contrast math when settings are saved. Links reach 4.5:1, dark-mode fills 3:1; a color that cannot reach 4.5:1 with either ink is refused with a suggested nearest passing shade. A contrast report is stored and shown in the customizer. |
| THM-006 | Font registry: 7 heading and 5 body families plus an accent script face, self-hosted WOFF2, Latin and Latin Extended subsets, OFL licenses recorded, at most 4 font files per page. |
| THM-007 | Render contract: SSR writes snapshot tokens as CSS custom properties and options as `data-*` attributes on the site root; components read nothing else. Layout breakpoints use container queries on the site root so preview and live pages match. |
| THM-008 | Layout options: five header styles (split, centered, banner, masthead, minimal), an optional announcement bar with an optional link, four hero styles (in column, full width, title overlay, split), sidebar right, left, or none, and text width 600–860px. Title overlay falls back to split when white text over the image's focal area is under 4.5:1. |
| THM-009 | Recipe card styles (classic, magazine, index card) and step styles (circles, big numerals, plain) change presentation only. Every style shows the fixed recipe features (jump, print, scaling, units, cook mode, nutrition disclaimer), and JSON-LD is identical across styles. |
| THM-010 | Post sections after the recipe card (after-card ad, share, end-of-post signup, ratings and comments, author box) can be reordered and, except ratings and comments on recipe posts, hidden. Gallery, inline signup, ad slots, and the related-posts row have on/off switches. Ad placement rules (MON-002) hold in every order. |
| THM-011 | Short text options (announcement 120, sign-off 40, newsletter heading 80, button and jump labels 24 characters) are plain text, sanitized, and HTML-encoded. |
| THM-012 | Design options are gated by plan through the entitlements facade (section 2.3). A downgrade keeps saved settings, renders gated options at their defaults, and restores them on upgrade. |
| THM-013 | Every preset stays within RND-007's budget; customization adds no JavaScript to public pages. |

### 4.7 SEO and discovery (SEO)

| ID | Requirement |
| --- | --- |
| SEO-001 | Every page has a unique title, meta description, canonical URL, Open Graph and Twitter tags, and Pinterest rich-pin markup. |
| SEO-002 | `sitemap.xml` (with image entries), `robots.txt`, and RSS feeds per site and per category are rebuilt after each publish. |
| SEO-003 | A redirect manager supports one-to-one 301s, bulk import, and loop and chain detection. |
| SEO-004 | Breadcrumbs render visibly and as `BreadcrumbList`. |
| SEO-005 | Search Console and Pinterest verification fields; IndexNow submission after publish. |
| SEO-006 | On-site search uses a prebuilt per-site index file; no SQL at query time. |
| SEO-007 | Suspended, trial-expired, and offline sites return correct status codes (503 with `Retry-After`, 410 for deleted). |

### 4.8 Audience (ENG)

| ID | Requirement |
| --- | --- |
| ENG-001 | Readers rate a recipe 1–5 stars, optionally with a review, without an account; one rating per reader per recipe, best-effort. |
| ENG-002 | Comments with one level of replies, author-marked replies, and states pending, approved, spam, and trash. First-time commenters are held by default. |
| ENG-003 | Spam defense: honeypot, minimum time-to-submit, rate limits, link limits, and a privacy-friendly challenge (DECIDE: Cloudflare Turnstile or hCaptcha). |
| ENG-004 | A moderation queue with bulk actions and an in-app and email digest. |
| ENG-005 | Newsletter signup forms collect email and optional first name, send double opt-in, store consent text, time, and source page, and honor unsubscribe. |
| ENG-006 | The subscriber list shows status and exports to CSV with consent metadata. CSV is the only export path. No sending (PP-D14). |
| ENG-007 | A contact form emails the blogger's verified address with the reader as Reply-To; the address is never shown on the page. |
| ENG-008 | Approved comments and ratings appear through cached islands within 60 seconds. |

### 4.9 Monetization (MON)

| ID | Requirement |
| --- | --- |
| MON-001 | Plus and Pro sites can enable AdSense with a publisher ID; the platform renders standard slots and `ads.txt`. |
| MON-002 | Pro sites can enable an allow-listed ad network with an editable `ads.txt`. Ads never render inside ingredient or step lists. |
| MON-003 | Affiliate links get `rel="sponsored nofollow"` and a disclosure above the first one. |
| MON-004 | A consent banner gates non-essential scripts; an IAB TCF v2.2 consent platform is used for EU and UK visitors when ads are on (DECIDE vendor). |
| MON-005 | Ad and analytics origins appear in the per-site CSP only when enabled. |

### 4.10 Analytics (ANL)

| ID | Requirement |
| --- | --- |
| ANL-001 | A cookie-free beacon records page views, referrer domain, country, device class, and search terms, with no IP stored. |
| ANL-002 | Daily aggregates are kept for the plan's retention; raw events are discarded within 48 hours. |
| ANL-003 | The dashboard shows views, top posts, referrers, search terms, ratings and comments, and subscriber growth. |
| ANL-004 | Optional GA4 loads only after consent. |

### 4.11 Billing and entitlements (BILL)

| ID | Requirement |
| --- | --- |
| BILL-001 | Checkout, plan changes, cancel, payment methods, and invoices use Stripe Checkout and the customer portal. |
| BILL-002 | Webhooks are signature-verified, stored, de-duplicated, and processed idempotently through the outbox; entitlements can be rebuilt from Stripe on demand. |
| BILL-003 | Entitlements cover sites, members, custom domains, branding removal, design options (THM-012), ads, ad networks, subscriber cap, storage, analytics retention, and import. |
| BILL-004 | Downgrade never deletes content; over-limit items become read-only or hidden with a plain explanation. |
| BILL-005 | After failed payment the site becomes `ReadOnly` for 14 days, then `Offline`, with content retained for 90 days (DECIDE durations). |
| BILL-006 | Trial reminders 3 days before and on the day; an expired trial site shows "coming soon". |
| BILL-007 | Tax is calculated by Stripe Tax. |
| BILL-008 | The operator can grant comp plans, extend trials, or override limits, each audited. |

### 4.12 Custom domains (DOM)

| ID | Requirement |
| --- | --- |
| DOM-001 | Custom domains are opt-in for paid plans (Plus: one per account; Pro: one per site; DECIDE), apex or `www`, verified by TXT record, with exact DNS instructions. |
| DOM-002 | Front Door managed certificates with automatic renewal and plain-language status. |
| DOM-003 | An active custom domain becomes canonical and the subdomain 301s to it; removal or downgrade reverses that with no downtime. |
| DOM-004 | A domain attaches to one site platform-wide; dangling domains are detected and released. |
| DOM-005 | If DNS stops pointing at PantryPress, the subdomain becomes canonical again and the blogger is notified. |

### 4.13 Publishing API and CreatorPantry connection (API)

| ID | Requirement |
| --- | --- |
| API-001 | Site Owners create, name, scope (`posts:write`, `media:write`, `posts:publish`), and revoke publishing keys. Keys are shown once and stored hashed. |
| API-002 | The **PantryPress Publish Format** is a documented, versioned JSON schema for a post with blocks, recipes (structured per RCP-001/002), media references, SEO fields, taxonomy, and an external source reference. |
| API-003 | Endpoints create or update a draft by external source reference, optionally publish or schedule it, read its status and live URL, and unpublish it. |
| API-004 | Media can be uploaded through the API or fetched from HTTPS URLs with SSRF protection, size limits, and malware scanning. |
| API-005 | Every write accepts an idempotency key; a replay returns the original result. |
| API-006 | The API is rate-limited per key and per site, audited, and counted against the same entitlements as the editor. |
| API-007 | Imported HTML or text is untrusted and passes the same sanitizer and validation as editor content. The blogger's wording is preserved. |
| API-008 | A "Connect CreatorPantry" screen walks the blogger through creating a key and pasting it into CreatorPantry. |
| API-009 | OpenAPI documentation and a contract test suite are published for API consumers. |

### 4.14 Import (IMP)

| ID | Requirement |
| --- | --- |
| IMP-001 | Import a WordPress export (WXR) of posts, pages, categories, tags, media, and comments. |
| IMP-002 | Import WP Recipe Maker and Tasty Recipes data into structured recipes, preserving the blogger's wording. |
| IMP-003 | Generate 301 redirects from old permalinks. |
| IMP-004 | Imports are resumable background jobs with a per-item report, sanitize all content, and never publish without confirmation. |

### 4.15 Security (PSEC)

| ID | Requirement |
| --- | --- |
| PSEC-001 | Public endpoints are rate-limited per IP, per site, and globally, with Front Door WAF custom rules at the edge and ASP.NET Core rate limiting at the origin. |
| PSEC-002 | Each site serves a strict, generated CSP plus HSTS, `X-Content-Type-Options`, `Referrer-Policy`, and `Permissions-Policy`. |
| PSEC-003 | The BFF cookie is `__Host-`, host-only, `Secure`, and `HttpOnly`; no tenant page can read or set it. |
| PSEC-004 | Public endpoints derive `SiteId` from the reader's requested host only (ARCH-003); no client-chosen header, address, or payload names a site, and reader payloads never carry a site identifier. |
| PSEC-005 | Reader personal data is encrypted at rest, minimized, and never shown to other readers except a commenter's display name. |
| PSEC-006 | Uploaded, imported, and API-supplied files are malware-scanned before they become public. |
| PSEC-007 | Isolation tests prove one site cannot read, render, cache, purge, or redirect to another site's content, including through host-header and cache-key tricks. |
| PSEC-008 | Publishing keys are hashed, scoped, revocable, rate-limited, and never logged. |

### 4.16 Non-functional (PNFR)

| ID | Requirement |
| --- | --- |
| PNFR-001 | Public page availability 99.9% monthly at the edge; admin and publishing 99.5%. |
| PNFR-002 | Cached TTFB under 200 ms p75; SSR cache miss under 500 ms p95 with warm page data; edge hit ratio 95% or more. |
| PNFR-003 | Core Web Vitals on a mid-range phone with ads off: LCP under 2.0 s, CLS under 0.05, INP under 200 ms at p75. |
| PNFR-004 | First-year capacity (DECIDE): 2,000 sites, 5 million page views a month, and one site spiking to 50 page views a second, absorbed by the edge. |
| PNFR-005 | A 2,000-post site rebuilds its page data in under 10 minutes; a full-site purge keeps SSR p95 under 1 second. |
| PNFR-006 | RPO 15 minutes and RTO 4 hours for SQL; page data is reproducible from published revisions. |
| PNFR-007 | The editor autosaves within 2 seconds of a pause and never loses typed content on refresh or network failure. |

---

## 5. Operational requirements

### 5.1 Azure topology

| Concern | Service | Shared with CreatorPantry? |
| --- | --- | --- |
| Edge, TLS, WAF custom rules, cache, custom domains | Azure Front Door Standard | **Profile shared**; separate endpoints, routes, and WAF policies |
| Compute | Azure Container Apps for Gateway, ApiService, Site, Worker, admin web host, and the `pantrypress-site` Node server | **Environment shared**; separate apps and managed identities |
| Database | Azure SQL Database on PantryPress's own logical server | No |
| Cache | Azure Managed Redis, PantryPress's own instance | No |
| Storage | PantryPress's own storage account: `media-originals` (private), `media-public` (via Front Door only), `pagedata` (private) | No |
| DNS | Azure DNS zone for `pantrypress.app` with a wildcard record | No |
| Secrets and identity | PantryPress's own Key Vault and managed identities | No |
| Networking | Origins accept only Front Door traffic (service-tag IP restriction, plus the edge secret; PP2.14); private endpoints for SQL, Redis, Blob, and Key Vault where the environment supports them | Environment VNet shared |
| Email | Azure Communication Services Email, PantryPress's own resource and sending domain | No |
| Observability | Application Insights and Log Analytics via OpenTelemetry | **Workspace shared**; separate cloud role names |
| Billing | Stripe, PantryPress's own account | No |

### 5.2 Operational requirements (OPS)

| ID | Requirement |
| --- | --- |
| OPS-001 | All infrastructure is code: Terraform, runnable from a workstation without a pipeline, in `docs/infrastructure/terraform/` (`bootstrap`, a shared `platform` stack, and a `pantrypress` stack; decided 2026-10-24). Environments: local (Aspire), `dev` in Azure (the staging role), and production. |
| OPS-002 | CI runs backend, frontend, theme golden-file, accessibility, performance budget, Rich Results, and publishing API contract tests on every PR. Production deploys are one approved, health-gated workflow with rollback. |
| OPS-003 | Migrations run explicitly before deployment; app startup never migrates. Public pages are unaffected by an app deploy. |
| OPS-004 | Alerts: edge 5xx, origin latency, page-data build failures and backlog age, webhook failures, email bounces, certificate failures, API error spikes, and Azure spend. Tuned so a normal week has none. |
| OPS-005 | A public status page for sites, publishing, and the admin app. |
| OPS-006 | An operator console to find sites by slug, domain, or account; view plan and usage; suspend or restore; rebuild page data; purge cache; revoke publishing keys; and read the audit trail. |
| OPS-007 | Abuse handling: report link on every site, takedown workflow, DMCA details, Safe Browsing checks on publish, and new-site velocity limits. |
| OPS-008 | SQL point-in-time restore (35 days), blob soft-delete and versioning, and a quarterly restore drill. |
| OPS-009 | Azure budgets with alerts at 50%, 80%, and 100%; storage metering feeds entitlements. |
| OPS-010 | Help center, in-app support with site context, and first response within one business day (DECIDE Pro target). |
| OPS-011 | Data subject requests: blogger export and erasure, plus erasure of a reader's comments, ratings, and subscriptions. |
| OPS-012 | Runbooks for site down, build backlog, Front Door or certificate failure, Stripe outage, email deliverability, abuse takedown, key compromise, and restoring a site version. |

### 5.3 Legal and compliance (CMP)

| ID | Requirement |
| --- | --- |
| CMP-001 | Terms of Service, Acceptable Use Policy, Privacy Policy, and a Data Processing Addendum (blogger is controller of reader data; PantryPress is processor). |
| CMP-002 | GDPR, UK GDPR, and CCPA/CPRA: consent records, minimization, export, erasure, and a sub-processor list. |
| CMP-003 | Subscriber consent: double opt-in, stored consent records, unsubscribe, and consent metadata in the CSV export. |
| CMP-004 | FTC affiliate disclosures (MON-003). |
| CMP-005 | Nutrition and dietary statements are shown as the blogger's own, with a disclaimer; PantryPress makes no health or allergen claims. |
| CMP-006 | Public themes meet WCAG 2.2 AA, with an accessibility statement page per site. |

---

## 6. Toolkit for the new repository

PP0 creates PantryPress's own `CLAUDE.md` and `.claude/` toolkit, modelled on CreatorPantry's but written for
PantryPress:

1. **CLAUDE.md** in SCRUB form: scope and out-of-bounds (section 1.3), the stack, the request seam, data zones
   (PP-D06), site isolation, public-tier rules, and usage commands.
2. **Rules:** `backend.md`, `tenancy.md` (site isolation), `auth.md`, `gateway.md`, `api-contract.md`,
   `frontend.md`, `design-system.md`, `recipes.md` (wording preservation, deterministic calculations),
   `content.md` (blocks, sanitization, published revisions), `media.md`, `sites.md`, `rendering.md`,
   `public-endpoints.md`, `publishing-api.md`, `billing.md`, `external.md`, and `aspire.md`.
3. **Skills:** `add-endpoint`, `add-site-entity`, `add-public-endpoint`, `add-recipe-feature`,
   `add-editor-block`, `add-media-feature`, `add-background-job`, `add-external-integration`,
   `add-theme-component`, `add-render-artifact`, `add-admin-component`, and `add-aspire-resource`.
4. **Read-only reviewers:** `architecture-reviewer`, `site-isolation-auditor`, `api-contract-checker`,
   `public-site-reviewer`, `design-review`, `integration-reviewer`, and `test-gap-analyzer`.
5. **Hooks:** secret scanning and formatting guards.

---

## 7. Milestones (phases for SCRUB prompts)

### 7.1 Phases

| Phase | Name | Delivers | Requirements |
| --- | --- | --- | --- |
| **PP0** | Repository and runtime foundation | Decisions record, toolkit, solution, AppHost, SQL, Redis, blob, migration service, Angular workspace, first run | ARCH-001, section 6 |
| **PP1** | Identity, gateway, and browser boundary | Accounts, BFF, cookies, antiforgery, edge hardening, idempotency, outbox, audit | ACCT-001–003, 005; PSEC-003 |
| **PP2** | Sites, membership, and tenancy | Site, slug, SiteHost, members and roles, site filters, two-site harness, public site API host | SITE-001–005, 008–010; ARCH-003, 015; PSEC-004, 007 |
| **PP3** | Design system and admin shell | `@pantrypress/ui` foundation, admin primitives, admin app shell, site switcher | PP-D07; THM-003 (admin) |
| **PP4** | Recipes and calculations | Recipe model, ingredient parsing, scaling, conversion, JSON-LD, recipe editor | RCP-001–009 |
| **PP5** | Posts, editor, and publishing | Block document, editor, revisions, autosave, recipe block, taxonomy, publish, schedule, preview | POST-001–010; BIZ-001 |
| **PP6** | Media library | Upload, scan, derivatives, alt text, focal point, usage, quota | MED-001–005; PSEC-006 |
| **PP7** | Page data and SSR rendering | Page-data builder and API, Angular SSR site, token contract, base layout with the Market preset, recipe card, print, pointer switch | RND-001–008; ARCH-004–008, 012–014; THM-007; THM-001 (Market only) |
| **PP8** | Site structure and SEO | Section library, home, archives, taxonomy pages, site pages with section and page blocks, page templates, menus, sitemaps, feeds, redirects, search | SEO-001–007; SITE-006–007, 011–012; POST-011 |
| **PP9** | Edge delivery | Front Door Standard, wildcard TLS, caching, purge, WAF custom rules, origin lockdown, CSP, degraded mode | PSEC-001–003; PNFR-001–002; POST-009 |
| **PP10** | Site settings and "Launch my blog" | Settings screens, preset picker with preview, onboarding flow | BIZ-002; THM-004; SITE-004, 006 |
| **PP11** | Site design and accessibility | Settings schema, color engine, font registry, snapshots, every customizer option, presets, customizer screen, combination tests, budgets, audits | THM-001–013; RCP-010; RND-007; CMP-006 |
| **PP12** | Ratings and comments | Public endpoints, spam defense, moderation, islands, `AggregateRating` | ENG-001–004, 008 |
| **PP13** | Newsletter signup and contact | Signup, double opt-in, subscriber list and CSV, contact form and contact page block, transactional email | ENG-005–007; POST-011 (contact block); CMP-003 |
| **PP14** | Subscriptions and entitlements | Stripe, webhooks, entitlements, trial, dunning states | BILL-001–008; ARCH-010–011 |
| **PP15** | Earning features | AdSense, ad-network allow-list, `ads.txt`, affiliates, consent | MON-001–005; CMP-004 |
| **PP16** | Analytics | Beacon, aggregates, dashboard, GA4 option | ANL-001–004 |
| **PP17** | Custom domains | Verification, managed certificates, canonical switching | DOM-001–005 |
| **PP18** | Publishing API and CreatorPantry connection | Keys, Publish Format, endpoints, media ingest, OpenAPI, contract tests, connect screen | API-001–009; ARCH-009; BIZ-010; PSEC-008 |
| **PP19** | Operations and launch readiness | Shared-platform and app IaC, CI/CD, operator console, abuse, alerts, status page, legal, drills, account deletion and export, launch gate | OPS-001–012; CMP-001–002; PNFR-003–007; BIZ-008–009; ACCT-004 |
| **PP20** | WordPress import | WXR and recipe-plugin import, media, comments, redirects | IMP-001–004; BIZ-011 |

### 7.2 Checkpoints

| After | Demonstrable result |
| --- | --- |
| **PP0** | `aspire run` shows SQL, Redis, blob, migrations, API, site API, gateway, worker, and the Angular apps healthy. |
| **PP2** | Two sites with similar data cannot read, mutate, cache, or enumerate each other; `demo.localhost` resolves to its site. |
| **PP5** | A blogger writes a post with a recipe card, autosaves, previews, and publishes it. |
| **PP7** | The published post renders through Angular SSR with a valid recipe card, print view, and scaling, with SQL stopped. |
| **PP9** | A staging site is live over HTTPS through Front Door and updates within 60 seconds of a publish. |
| **PP10** | A new account completes "Launch my blog" and has a live post in under 30 minutes. |
| **PP11** | A blogger restyles a site from a preset with the customizer, previews, applies, and reverts; every preset passes accessibility and budget gates. |
| **PP14** | A trial converts through Stripe; downgrade and non-payment never lose content. |
| **PP18** | CreatorPantry (or the contract test client) publishes a post with a recipe into a site using a publishing key, idempotently. |
| **PP19** | Production launch gate passes. |

### 7.3 Work on the CreatorPantry side (separate repository)

These belong in CreatorPantry's own prompt library, not this one, and depend on PP18:

1. A "PantryPress" publishing profile that stores a per-site publishing key as a secret.
2. A mapper from CreatorPantry recipes and long-form drafts to the PantryPress Publish Format.
3. A `pantrypress` adapter on CreatorPantry's publishing ledger that calls the PantryPress publishing API with
   idempotency keys, plus reconciliation of status and live URL.
4. CreatorPantry's IaC referencing the shared Front Door profile, Container Apps environment, and monitoring
   workspace (PP-D16).

---

## 8. Open decisions

| ID | Question | Default assumed |
| --- | --- | --- |
| PP-D07 | One design-system library with two entry points, or two libraries | One library, two entry points |
| PP-D08 | Admin app SSR | Client-rendered SPA behind the BFF |
| PP-D09 | Tier prices and limits | Section 2.3, prices TBD |
| PP-D10 | Free-forever Starter or trial | 14-day trial |
| PP-D11 | Ad networks on the allow-list | AdSense plus one network chosen at PP15 |
| PP-D13 | Front Door Premium upgrade threshold | Sustained attack or revenue threshold |
| PP-D16 | Owner of shared Azure resources | **Decided 2026-10-24:** the Terraform `platform` stack in the PantryPress repo |
| POST-001 | Editor library | TipTap (ProseMirror) through an Angular wrapper |
| DOM-001 | Custom-domain allowance per plan | Plus: one per account; Pro: one per site |
| D-13b | Custom domains beyond one Front Door Standard profile (100 per profile) | A second profile, or Premium, before 80 on a profile; PP17.1 checks the platform-wide count |
| ENG-003 | Spam challenge vendor | Cloudflare Turnstile |
| MON-004 | Consent platform vendor | Chosen at PP15 |
| ACCT-003 | Disposable-email blocking at sign-up | Yes, with a maintained list |
| — | Preset names and visual direction | Six presets from the Studio mockup, Market built in PP7, the rest in PP11 |
| PP-D17 | Which Studio options are Plus versus Pro | Plus gets all but textures and the Banner and Masthead headers |
| THM-002 | Logo upload formats | Raster only (PNG, WebP); SVG only with a strict sanitizer |
| THM-010 | Can ratings and comments be hidden on posts without a recipe | Yes |
| POST-011 | Emit `FAQPage` JSON-LD for FAQ blocks | Off by default, blogger opt-in per block |
| — | Capacity and KPI targets | As stated |

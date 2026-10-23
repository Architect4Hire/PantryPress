# PantryPress: Decisions Record

The index of architectural and product decisions for PantryPress. Created by prompt PP0.1 and approved by the
owner on 2026-10-23. Each decision is a numbered architecture decision record (ADR) in this folder; ADR
`NNNN` records decision `PP-DNN`. Where an ADR and another document disagree, stop and reconcile them before
writing code (CLAUDE.md).

**Sources at the time of recording**

| Document | Version |
| --- | --- |
| `docs/requirements/pantrypress-requirements.md` | draft v2.1, 2026-10-22 |
| `docs/requirements/pantrypress-customization-spec.md` | v1.2, 2026-10-22, reconciled on 2026-10-23 (section 5) |
| `docs/prompts/pantrypress-scrub-prompts.md` | 21 phases (PP0–PP20), 257 microprompts |

**Status values**

- **Accepted:** decided by the owner on 2026-10-22, or confirmed at PP0.1 on 2026-10-23.
- **Accepted, with open items:** the decision stands; a DECIDE item inside it is still open (section 3).
- **Proposed:** the whole decision is a DECIDE item. Later prompts build on its default, which is *not* a
  decision until the named prompt closes it.
- **Superseded by NNNN:** replaced by a later ADR. Kept for history.

---

## 1. Owner decisions (requirements section 1.2)

Decided by the owner on 2026-10-22. The ADRs below carry these into architecture.

| Topic | Decision | ADR |
| --- | --- | --- |
| Product independence | PantryPress is a standalone product. Using it does not require CreatorPantry. CreatorPantry is an optional source that publishes into PantryPress through the publishing API. | 0001, 0015 |
| Repository | A new repository with its own solution, Aspire AppHost, domain, toolkit, and CI. No shared code with CreatorPantry. | 0001 |
| Azure | Shared with CreatorPantry only for the Front Door profile, Container Apps environment, and monitoring workspace. PantryPress has its own SQL server, Redis, storage, and Key Vault. | 0016 |
| Stack | ASP.NET Core and Aspire on the back end; Angular 22 on the front end, with SSR for public blog pages. | 0001, 0008 |
| Public rendering | Angular SSR on every request, cached at Azure Front Door. | 0002 |
| Edge | Azure Front Door Standard. | 0013 |
| Hosting model | Multi-tenant SaaS on Azure. Every site gets an automatic `{name}.pantrypress.app` subdomain. | 0003, 0013 |
| Custom domains | Opt-in for paid plans. Everyone else stays on the subdomain. | 0013 |
| Newsletters | Signup forms and subscriber CSV export only. No configuring or sending newsletters, and no sync to email services. | 0014 |
| Site design | Six presets as starting points, plus a customizer of typed settings. No custom CSS, HTML, templates, scripts, or font uploads. | 0017 |
| Design system | A new PantryPress design system. Nothing shared with `@creator-pantry/ui`. | 0007 |
| Audience | Hobby bloggers bordering on prosumer. | — |
| Revenue | Subscription tiers. | 0009, 0010 |
| Extensibility | Closed set. No plugins, third-party themes, or custom code. Design freedom comes from typed settings. | 0011, 0017, 0018 |
| Team and schedule | One developer using Claude Code. Open timeframe; phases ordered by value and risk. | — |

---

## 2. Architecture decision records

| ADR | Title | Status | Date | Open items |
| --- | --- | --- | --- | --- |
| [0001](0001-own-repository-and-aspire-application.md) | Own repository and Aspire application | Accepted | 2026-10-22 | — |
| [0002](0002-cache-first-ssr-delivery.md) | Cache-first delivery with Angular SSR on every request | Accepted, with open items | 2026-10-22 | D-02 |
| [0003](0003-site-is-the-tenant.md) | The site is the tenant | Accepted | 2026-10-23 | — |
| [0004](0004-publishing-is-a-snapshot.md) | Publishing is a snapshot | Accepted | 2026-10-23 | — |
| [0005](0005-structured-recipes-deterministic-calculations.md) | Structured recipes and deterministic calculations | Accepted | 2026-10-23 | — |
| [0006](0006-data-zones.md) | Data zones | Accepted | 2026-10-23 | — |
| [0007](0007-pantrypress-design-system.md) | A new design system, `@pantrypress/ui` | Accepted, with open items | 2026-10-22 | D-07 |
| [0008](0008-public-site-angular-ssr.md) | Public themes are an Angular 22 SSR application | Accepted, with open items | 2026-10-23 | D-08 |
| [0009](0009-stripe-subscriptions-in-house-entitlements.md) | Subscriptions via Stripe, entitlements in-house | Accepted, with open items | 2026-10-23 | D-09 |
| [0010](0010-trial-instead-of-free-tier.md) | Trial instead of a free-forever tier | Proposed | 2026-10-23 | D-10 |
| [0011](0011-third-party-script-allow-list.md) | Third-party scripts are an allow-list | Accepted, with open items | 2026-10-23 | D-11 |
| [0012](0012-no-ai-in-pantrypress.md) | No AI anywhere in PantryPress | Accepted | 2026-10-23 | — |
| [0013](0013-domains-cookies-and-edge.md) | Domains, cookies, and the edge | Accepted, with open items | 2026-10-22 | D-13 |
| [0014](0014-newsletter-signup-and-export-only.md) | Newsletter signup and CSV export only | Accepted | 2026-10-22 | — |
| [0015](0015-publishing-api.md) | The publishing API is how other tools publish into PantryPress | Accepted | 2026-10-23 | — |
| [0016](0016-shared-azure-separate-data.md) | Shared Azure, separate data | Accepted, with open items | 2026-10-22 | D-16 |
| [0017](0017-studio-site-customization.md) | Studio site customization | Accepted, with open items | 2026-10-22 | D-17, THM-001, THM-002, THM-010 |
| [0018](0018-section-based-pages.md) | Section-based pages, no page builder | Accepted, with open items | 2026-10-22 | POST-011 |

---

## 3. Open DECIDE items

Later prompts assume these defaults. Close each item at the named prompt (section 6).

| Ref | Question | Options | Impact | Default assumed | Closes at |
| --- | --- | --- | --- | --- | --- |
| D-02 | Can Front Door serve stale content on origin errors? (ADR 0002) | Serve stale on error; long TTL plus explicit purge only | How cached pages behave during an SSR or site API outage | Long TTL plus purge until stale-on-error is verified | PP9.4 |
| D-07 | Design system packaging (ADR 0007) | One library with two entry points; two libraries | One library shares tokens simply but needs a lint rule keeping admin code out of the SSR bundle | One library, two entry points | PP3 |
| D-08 | Public hydration if the budget fails (ADR 0008) | Angular incremental hydration; hydration off with small vanilla island scripts | Whether the Angular runtime fits the 100 KB RND-007 budget | Incremental hydration, vanilla fallback if PP7.2 measures a breach | PP7.2 |
| D-09 | Tier prices and limits (ADR 0009) | Section 2.3 proposal or revised numbers | Configuration only | Section 2.3 limits; prices to be set | PP14 |
| D-10 | Free-forever Starter or trial (ADR 0010) | Free Starter; 14-day trial then paid | A free subdomain tier is the main abuse and cost risk | 14-day trial, no free-forever tier | PP14 |
| D-11 | Ad networks on the allow-list (ADR 0011) | AdSense only; AdSense plus named networks | Each network adds script origins to the per-site CSP | AdSense plus one network chosen at PP15 | PP15.5 |
| D-13 | Front Door Premium upgrade trigger (ADR 0013) | Attack-driven; revenue-driven | Operations only; no build impact | A sustained attack the custom rules cannot handle, or revenue that makes the Premium base fee small | Operations |
| D-16 | Owner of shared Azure resources (ADR 0016) | `infra/platform` in this repo; CreatorPantry's repo; a separate platform repo | Which IaC creates the shared profile, environment, and workspace | `infra/platform` in this repo; CreatorPantry references them as existing | PP19 |
| D-17 | Studio options for Plus versus Pro (ADR 0017) | Any split | Entitlement configuration | Plus gets all presets and options except textures and the Banner and Masthead (`stacked`) headers; Pro gets all | PP11 |
| THM-001 | Preset names and looks (ADR 0017) | Final names and looks | Golden files and presets | Market, Hearth, Larder, Farmhouse, Sunny, Supper Club, from the Studio mockup | PP7 (Market), PP11 (the rest) |
| THM-002 | Logo upload formats (ADR 0017) | Raster only; SVG with a strict sanitizer | SVG adds a sanitizer and its attack surface | Raster only (PNG, WebP); SVG only with a strict sanitizer | PP11 |
| THM-010 | Can ratings and comments be hidden on posts without a recipe? (ADR 0017) | Yes; no | Section visibility rules | Yes | PP11 |
| POST-011 | Emit `FAQPage` JSON-LD for FAQ blocks (ADR 0018) | On; off; blogger opt-in | Structured data and search risk | Off by default, blogger opt-in per block | PP8 |
| POST-001 | Editor library | TipTap (ProseMirror) through an Angular wrapper; alternatives | Block schema and editor UI | TipTap through an Angular wrapper | PP5.1 spike, then the owner decides |
| DOM-001 | Custom domains per plan | Any allowance | Entitlements | Plus: one per account; Pro: one per site | PP17 |
| ENG-003 | Spam challenge vendor | Cloudflare Turnstile; hCaptcha | Script origins and the gateway | Cloudflare Turnstile | PP12 |
| MON-004 | Consent platform vendor (IAB TCF v2.2) | Any certified CMP | Script origins and consent integration | Chosen at PP15 | PP15.2 |
| ACCT-003 | Disposable-email blocking at signup | Yes; no | Abuse | Yes, with a maintained list | PP1 |
| BILL-005 | Failed-payment durations | Any durations | Billing states | `ReadOnly` for 14 days, then `Offline`; content kept for 90 days | PP14 |
| SITE-004 | Slug renames allowed per year | Any number | Abuse and redirect volume | **None stated.** PP10.8 must stop and ask. | PP10.8 |
| OPS-001 | Infrastructure-as-code approach | `azd`-generated Bicep plus `infra/platform`; alternatives | Deployment | `azd` Bicep plus `infra/platform` | PP19 |
| OPS-010 | Pro support response target | Any target | Operations | First response within one business day | PP19 |
| KPI, PNFR-004 | Success targets and first-year capacity | Any targets | Load tests and alerts | As stated in requirements 2.5 and PNFR-004 | PP19 |

### DECIDE items local to one prompt

These appear only in the named prompt. The prompt closes them and records the outcome here.

| Prompt | Question | Default assumed |
| --- | --- | --- |
| PP1.9 | Idempotency-key expiry | 24 hours |
| PP6.7 | Grace period before unused media derivatives are deleted | 7 days |
| PP7.3 | Allow-listed video hosts for iframes | None stated; PP7.3 must ask |
| PP7.12 | Page-data versions kept per site | 5 |
| PP8.10 | Search index size budget per site | None stated; PP8.10 must ask |
| PP13.4 | Lifetime of confirm and unsubscribe tokens | 7 days |
| PP15.5 | First allow-listed ad network | Chosen at PP15.5 (D-11) |
| PP18.6 | Publishing-API update when the draft was edited in the editor | None stated (conflict or new revision); PP18.6 must ask |
| PP19.8 | Behavior when Safe Browsing is down | Fail open to a held-for-review state, never silently |

---

## 4. Decision log

| Date | Change |
| --- | --- |
| 2026-10-22 | Owner decisions (section 1), PP-D01, PP-D02, PP-D07, PP-D13, PP-D14, PP-D16, PP-D17, PP-D18 |
| 2026-10-23 | PP0.1: ADRs 0001 to 0018 written. All accepted except 0010 (proposed, D-10). PP-D08 admin app confirmed client-rendered. Open items recorded with defaults (section 3). Customization spec reconciled (section 5). |
| 2026-10-23 | PP0.2: toolkit aligned with this record (section 5). No decision changed. ADR 0008 and 0012 consequences note the widened hook coverage. |
| 2026-10-23 | PP0.3: solution file is `PantryPress.slnx`; tests use xUnit. No decision changed. |
| 2026-10-23 | PP0.4: own SQL, Redis, and blob storage declared with `pantrypress-` volumes (PP-D16 locally). No decision changed. |

---

## 5. Document reconciliation

### At PP0.1

Applied to the customization spec and the requirements on 2026-10-23, following the requirements as the
functional source of truth:

- Spec section 15: Starter design gating now matches requirements 2.3 (Market and Hearth presets, mode, logo,
  tagline, brand and highlight color). The old spec said "Presets only".
- Spec section 15: removed the stale "v1 options" row and "(if PP-D17 adopted)". Named the gated headers
  "Banner and Masthead (`stacked`)" to match the requirements.
- Spec section 19: removed the question about v1 themes becoming presets, which the spec's status note already
  answers.
- Spec header and requirements line 18: mockup and spec paths now point to the repository
  (`docs/design/mockups/`, `docs/requirements/`).

Left for PP0.2 (toolkit alignment), resolved there:

- `.claude/rules/tenancy.md` rule 4 uses `/api/sites/{siteSlug}/…`; `api-contract.md` and the `add-endpoint`
  skill use `/api/v1/sites/{siteSlug}/…`.
- `README.md` says `src/` is created in PP0.7 and `web/` in PP0.11. The prompts create them in PP0.3 and
  PP0.7.

### At PP0.2 (toolkit alignment)

Applied to CLAUDE.md, README.md, and `.claude/` on 2026-10-23 with owner approval:

- `tenancy.md`: admin route is `/api/v1/sites/{siteSlug}/…`.
- `billing.md`: the 14-day trial is labelled the D-10 default (ADR 0010 proposed), not a decision.
- `rendering.md`: hydration islands labelled the D-08 default; FAQPage opt-in labelled the POST-011 default.
- `backend.md`: `Platform` added to the module list (ADR 0003 depends on `Modules/Platform/`).
- `auth.md`: email verification gates publishing, not site creation (ACCT-001, PP1.2).
- `invariant-guard.mjs`: the `Tests/` exemption for `IgnoreQueryFilters()` removed (ADR 0003); AI SDK
  coverage widened to more package names plus imports and model endpoints in `src/` (ADR 0012); web storage
  also blocked in `@pantrypress/ui/themes` (ADR 0008).
- `format-guard.mjs`, CLAUDE.md usage, README: the Angular workspace is `src/web` (ADR 0001, PP0.7).
- README and `.claude/README.md`: prompt numbers corrected (PP0.3, PP0.7, PP0.8; PP0.2 owns the toolkit).

Found at PP0.2, outside the toolkit. Reconcile at the named prompt before writing its code:

- PP18.1 asks for "a slow hash" of publishing keys; ADR 0015 and `auth.md` say salted SHA-256.
- PP2.5 allows `IgnoreQueryFilters()` in "operator and erasure paths"; ADR 0003 allows it only in
  `Modules/Platform/`, so cross-site erasure code must live there.
- PP0.3 does not name the solution file. CLAUDE.md and `format-guard.mjs` expect `PantryPress.sln`; .NET 10
  may default to `.slnx`. **Resolved at PP0.3:** the owner chose `PantryPress.slnx`; CLAUDE.md and
  `format-guard.mjs` were updated to match.
- AI SDK installs through a shell command (`npm install`, `dotnet add package`) bypass the write hooks; the
  next manifest edit or a reviewer catches them.

---

## 6. Changing a decision

- **Closing an open item:** update its row in section 3 with the outcome and date, and add the outcome to the
  ADR's "Open items" section. If the item was the whole decision (ADR 0010), change the ADR's status to
  Accepted. Add a row to the decision log.
- **Changing an accepted decision:** do not rewrite the ADR. Add a new ADR with the next number, mark it
  "Supersedes NNNN", and change the old one's status to "Superseded by NNNN". New decisions take numbers from
  0019 onward, and their decision IDs continue from PP-D19.
- **In both cases:** update the requirements, spec, or prompts the change affects in the same change, and
  realign any rule, skill, or CLAUDE.md text it contradicts.

# PantryPress — Platform Landing Page SCRUB Prompts

Atomic prompts for building PantryPress's own public landing page at the root of `pantrypress.app`, with Claude Code
and the PantryPress `.claude/` toolkit. It is scoped to one observable outcome: an anonymous visitor to
`pantrypress.app` sees an honest, fast, accessible page about PantryPress that leads to sign-up and sign-in on the same
address, and a signed-in visitor goes straight to the dashboard instead.

The document is modelled on CreatorPantry's landing work: `creatorpantry-landing-page-scrub-prompts.md` (L.1–L.9) and
`creatorpantry-landing-pricing-scrub-prompts-extensions.md` (LP.1–LP.12). It copies the **craft** of that page (section
structure, visual ambition, motion rules, honesty rules, the accessibility and performance bar, and the
prompt-by-prompt way it was built and verified), not its **claims**. Every headline, card, FAQ answer, and price
here traces to something PantryPress ships, a prompt that will ship it, or a dated owner decision.

It supplements `docs/prompts/pantrypress-scrub-prompts.md` (the "main prompts") and does not redefine anything in
`CLAUDE.md`, `.claude/rules/`, or the decision record. The address it lives at is ADR 0019 (PP-D19).

## Reusable SCRUB skeleton

```text
SCOPE:        one observable change and the exact repository seam it touches
CONSTRAINT:   stack, rule files, skills, requirement IDs, and prerequisites
RESTRICTION:  explicit exclusions and invariants that must not be weakened
UTILIZATION:  skills, read-only reviewers, and tools to invoke
BEHAVIOR:     inspect, plan, wait for approval, implement, test, and report
```

Run one prompt at a time, in order, at the point the "When each prompt runs" table gives it. Mark a prompt `- done`
only after its verification passes, and add a delivery note under it when what shipped differs from what was asked.

## The ask

Owner, 2026-10-28: "write a new landing page set of scrub prompts based upon the landing page for our sister
application creatorpantry.com - make sure to make these prompts work for this app based upon the scrub prompts and
architecture." Then, the same day: "planned is fine since this is still very early in development - and will help
shape the development of the product in all honesty, the page should live at the root of pantrypress.app and the sign
in and register would be routes off of that and once we are signed in we would direct to the actual app - we need to
update everything to reflect that decision." And: "I wanted brand unity"; "all pages should be built by the angular
app"; "same fonts"; "everything as recommended".

| ID | Requirement | Prompts |
| --- | --- | --- |
| PPL-001 | `pantrypress.app` serves the landing page, the auth screens, and the dashboard from one origin. A signed-in visitor at `/` or `/pricing` reaches `/dashboard` before any marketing is sent. | PPL.1, PPL.2, PPL.3, PPL.22 |
| PPL-002 | Every claim traces to a source and carries its status. A planned capability is shown as **Coming soon**, never as available; nothing that ships is undersold. | PPL.4, PPL.5, PPL.9, PPL.10, PPL.14 |
| PPL-003 | The page shows the real product (the dashboard and a blog as readers see it) from screenshots of sample data only. | PPL.11, PPL.12 |
| PPL-004 | The page belongs to the CreatorPantry family (the robot-chef logo style, Fredoka and DM Sans) as PantryPress's own build, at CreatorPantry's level of craft. | PPL.6, PPL.7, PPL.8 |
| PPL-005 | Pricing shows every plan, monthly and annual, from the server's catalogue, behind a flag, hidden cleanly without it; `/pricing` opens the page at that section. | PPL.15, PPL.16 |
| PPL-006 | An FAQ answers only what has a sourced answer. | PPL.17 |
| PPL-007 | No fabricated proof of any kind; trust comes from the mission and the owner's stage line. | PPL.18 |
| PPL-008 | The platform legal pages exist on the apex before pricing goes live; until then their links are plain text marked "(coming soon)". | PPL.19 |
| PPL-009 | A scraper or a reader without JavaScript gets the full page with its title, description, canonical, and social card; the site stays unindexed until launch. | PPL.2, PPL.20 |
| PPL-010 | The page meets its budget on mobile Lighthouse (LCP < 2.5 s, CLS < 0.1, first-load images < 600 KB), measured by a committed harness. | PPL.21, PPL.22 |
| PPL-011 | WCAG 2.2 AA in both themes at 375, 768, and 1440 px and at 200% zoom, keyboard-only, with reduced motion respected. | PPL.6, PPL.22, and every component prompt |
| PPL-012 | Nobody is invited to sign up before sign-up is open (BIZ-009); the page says when it opens instead of failing on submit. | PPL.13 |

## Decisions already made

Owner, 2026-10-28, unless dated otherwise.

1. **The address is ADR 0019.** `pantrypress.app` serves `/`, `/pricing`, the legal pages, `/sign-in`, `/sign-up`,
   `/verify-email`, `/reset-password`, and the dashboard under `/dashboard/…`, all through the gateway on one origin.
   `app.pantrypress.app` and `www.pantrypress.app` 301 to the apex. Locally the platform home is `localhost`; dev is
   `dev.pantrypress.app`.
2. **One Angular app builds every page.** `pantrypress-admin` prerenders `/`, `/pricing`, and the legal pages at build
   and renders everything else in the browser. No Node server runs for the apex.
3. **A signed-in visitor is redirected by the gateway**, from its own cookie, before any HTML: no client-side
   "anonymous-only" guard and no session round trip before the first paint (CreatorPantry measured that round trip as
   free, but here it is not even owed).
4. **Planned is fine.** PantryPress is early, and the page will help shape the product. Every claim carries a tag
   (below). A planned capability may be shown, with **Coming soon** in words on its badge, never by colour alone, and
   the copy document names the prompt that ships it.
5. **Brand unity with CreatorPantry.** The robot-chef logo (`docs/images/logo.png`, `logo-dark.png`) is family
   branding and is used as is. The fonts are the same: Fredoka for display and DM Sans for reading, **self-hosted**
   (`design-system.md` bans remote fonts, and self-hosting avoids the Google disclosure CreatorPantry still has open).
   The palette leads with the logo's teal and orange, with green for actions. The voice is CreatorPantry's warm and
   direct. It is built as PantryPress's own copy in `@pantrypress/ui`: nothing is imported from `@creator-pantry/ui`
   (ADR 0007). **The robot never does the work in the copy**: PantryPress has no AI (PP-D12), so no line says or
   implies that anything is generated, suggested, or written for the blogger.
6. **Prompts are `PPL.n`, requirements `PPL-nnn`.**
7. **No navigation bar.** CreatorPantry built a sticky bar and removed it because it spoiled the hero; moving it under
   the hero was "very jarring". The hero carries **Sign in**.
8. **CreatorPantry on the page:** one **Coming soon** card, "Publish from CreatorPantry", which ships only when PP18
   (the publishing API) and a CreatorPantry adapter both exist (neither does today); an FAQ answer, "Do I need
   CreatorPantry? No." (BIZ-010, decided); and a footer line, "From the makers of CreatorPantry", linking to
   `https://creatorpantry.com`. How it works shows CreatorPantry only once the link works end to end.
9. **Pricing comes from the server's catalogue** (PP14.1 plus PPL.15's anonymous route), behind `Landing:Pricing`, off
   in every deployment until the reviewed legal pages are published. "Recommended for most food bloggers" marks the
   plan the catalogue flags (Plus, per `docs/features/plans/`). A trial length is shown only once D-10 is accepted and
   enforced. The figures in `docs/features/plans/` and `docs/pricing/` are recommendations and are never typed into
   code or copy.
10. **Sign-up opens at launch.** Until the BIZ-009 abuse controls ship (PP12.3, PP19.7, PP19.9), `Accounts:PublicSignUp`
    is off and the primary CTA reads "Sign-up opens at launch" rather than linking to a form that refuses. No waitlist:
    collecting emails would be a new feature.
11. **Stage line:** "PantryPress is early in development, and we're building it in the open." No launch date.
    **Contact:** robert@pantrypress.app. **Legal pages:** content from PP19.13 as markdown in `docs/legal/`, rendered by
    PPL.19; plain text marked "(coming soon)" until then. **`robots.txt`:** `Disallow: /` until launch.
12. **Sections.**
    - **Keep:** hero, value props, product tour, how it works, pricing, FAQ, trust, final CTA, and footer.
    - **Add:**
      - "Your blog, as readers see it": a captured sample blog on a phone, showing the recipe card, jump to recipe,
        and scaling;
      - a preset gallery of the sample blog in each preset;
      - "Your content stays yours": export at any time (BIZ-006), a downgrade never deletes anything (BILL-005), and
        no plugins or updates (BIZ-003).
    - **Drop** (nothing true to say about PantryPress): credits, the plan estimator, packs, the draft-versus-final
      comparison, and the AI proposal card.
    - **Not its own section:** WordPress import (PP20) is a **Coming soon** value-prop card.
13. **Photographs.** CreatorPantry's twelve food photographs are the owner's own and are reused, copied once as image
    files. There is no code, package, or runtime dependency on CreatorPantry.
14. **Tour captures** photograph only the sample account `tour-capture@pantrypress.local` and the sample sites named in
    the capture script's `SAMPLE_SITES` list.

### Tags every claim carries (PPL.5)

| Tag | Means | Can render? |
| --- | --- | --- |
| **Shipped** | Code and tests on `dev` today; the path is the evidence. | Yes |
| **Decided** | A dated owner decision or an accepted ADR, not yet built. | Only as a promise the decision makes ("Your content is always yours to export"), never as a feature on screen |
| **Planned (PPx.y)** | A prompt in the main prompts will ship it. | Yes, with a **Coming soon** badge |
| **Catalogue** | A value read from the plan catalogue at runtime. Copy names the field, never the number. | Behind `Landing:Pricing` |
| **Not built** | No prompt ships it. | No |

### Inherited rules

These are CreatorPantry's delivery notes, rewritten for PantryPress. The prompts below cite them by number.

- **IR-1 No fabricated proof.** No testimonials, customer or site counts, ratings, press logos, or "most popular"
  badges. With no real proof, say so ("We don't have bloggers' stories to share yet. When we do, they'll be real
  ones.") and treat real stories as a follow-up.
- **IR-2 No claim without a source.**
  - Each claim names a file, a test, a prompt, or a dated owner decision.
  - A planned claim is badged; a shipped one is never left badged. CreatorPantry shipped stale "In the works" labels
    and undersold itself.
  - No section is built for a feature that has nothing to show. CreatorPantry built a draft-versus-final band and took
    it out for exactly this reason.
  - The words that overclaimed at CreatorPantry don't come back in new form: "import" when only part of it exists, and
    "connect your channels" when the connection is configured by the operator.
- **IR-3 Prices come from the server.** No price, credit, or limit is typed in TypeScript, a template, the copy, or the
  prerendered HTML.
  - The catalogue is fetched once and shared by every section that reads it. CreatorPantry fired four identical
    requests per load.
  - A failure hides the section and logs nothing to the visitor. The page says why only to an operator: see PPL.16.
- **IR-4 Tour screenshots show sample data only.**
  - The capture script refuses any account but the sample one and any site outside `SAMPLE_SITES`.
  - It refuses a screen whose proof of work is not *visible*: being in the DOM is not enough, so the proof is scrolled
    into view first.
  - A screen that exists only at an id URL is never photographed as its hub page (`requiresUrl`).
  - Images are AVIF and WebP at 1x and 2x, in both themes.
  - The tour renders nothing until something has been captured, and a card links only to a tour stop that exists.
  - The script's drift checks fail loudly. CreatorPantry's read a marker that no longer existed and passed by accident.
- **IR-5 Motion is progressive enhancement.**
  - CSS never hides a section to wait for JavaScript; a reveal writes inline styles only once it has decided to.
  - Nothing moves under `prefers-reduced-motion`, and pointer-driven drift stops on coarse pointers.
  - Staggers are capped at six steps of 60 ms. Everything reveals on `beforeprint`.
  - The hero is never animated in: it holds the LCP element.
  - A paused or reduced-motion control is not rendered rather than hidden.
- **IR-6 Performance budget on mobile Lighthouse.**
  - Targets: LCP under 2.5 s, CLS under 0.1, first-load images under 600 KB counted at 412 px with no scroll.
  - Measure a production build served as the gateway serves it, never the dev server.
  - **Commit the harness.** CreatorPantry's LP.12 could not reproduce LP.11's numbers because LP.11 never committed one.
  - The LCP image is fixed and preloaded, never discovered only after a lazy chunk runs.
  - The hero photos are chosen deterministically: random photos cannot be budgeted or preloaded.
- **IR-7 Accessibility.**
  - WCAG 2.2 AA in both themes at 375, 768, and 1440 px and at 200% zoom; keyboard-only operation.
  - One `h1` and no skipped heading levels.
  - Targets at least 40 px.
  - Decorative images get empty alt text and reserved sizes.
  - The footer sits outside `<main>` so it keeps its `contentinfo` role.
  - Tabs follow the WAI-ARIA tabs pattern.
  - Status is told in words, never by colour alone.
  - axe runs in the specs and the journey. CreatorPantry had no automated accessibility checks, and frontend.md asks
    for them here.
- **IR-8 Section addresses.**
  - `/pricing` scrolls without animation after `document.fonts.ready`.
  - It focuses the section's *heading*, with a visible ring sized to the words, not the whole section.
  - It does nothing when the section did not render.
- **IR-9 Readable type.** One body size across the public page; headings step up from it. Controls and one-word
  badges are the only exceptions.
- **IR-10 Legal links.** Plain text with "(coming soon)" until a reviewed page exists; pricing does not go live
  without them.
- **IR-11 Verify through real routing, guards, and the gateway.** Never by instantiating a component to prove routing.
- **IR-12 No third-party scripts, analytics, or trackers** without a separate decision. PantryPress's own cookie-free
  analytics is site-scoped and does not apply to the apex. No checkout on the landing page: a plan's CTA goes to
  sign-up carrying the plan's key.
- **IR-13 No AI language** (PP-D12). No "generate", "AI", "smart", "auto-write", or "suggest" for anything PantryPress
  does. The robot in the logo is a mascot, not a feature. A spec scans the landing copy for the words, as
  CreatorPantry's `check-safety-copy` scans its FAQ.
- **IR-14 Plain language** (CLAUDE.md). No DNS, caches, blobs, page data, or HTML in copy, except the custom-domain
  card, which may say "your own domain".

## How this list was built

The PantryPress repository was read on 2026-10-28 at `ff41df3` ("Prompt PP2.12"), plus PP2.13's uncommitted work in the
tree. CreatorPantry was read the same day at `d72f5cd` ("Prompt AP.5.1"), read-only. Find each file again before
trusting a line number.

### Reference inventory (CreatorPantry)

| Section | What it does | From its design system | Motion | Tests | Lesson its delivery note records |
| --- | --- | --- | --- | --- | --- |
| Routing | `/` and `/pricing` behind `anonymousOnlyGuard`. A signed-in visitor goes to `/app`. The landing route is the one eager route. | — | — | `app.routes.integration.spec.ts` with the real guards | An eager landing chunk saved 1.0 s of LCP, and the guard's session wait was free. **Here the gateway redirects instead** (decision 3). |
| Hero | One `h1` at the display size, with one phrase highlighted by an orange underline painted as a background. Logo `fetchpriority=high`, two CTAs. | Display type token, `cpButton` lg | None | Copy, a single highlight, logo attributes, CTA hrefs | A pseudo-element underline made a collage photo the LCP candidate. The logo needed intrinsic sizes (CLS 0.379). |
| Hero collage | Three fixed photos (AVIF and WebP at 480 and 960, with a JPEG fallback), colour blocks, a proposal capture. `aria-hidden`. | Soft fruit tokens | Pointer parallax only under `no-preference` and `pointer: fine`, written to CSS variables with no change detection; paused off-screen | Drift on and off, alt text, sizes | Photos dealt at random could not be preloaded or budgeted, so they were fixed. |
| Value props | Nine cards, badge text plus tone, an optional tour link only for captured stops. | `cpCardGrid`, `cp-card`, `cp-badge` | Staggered reveal | Badges and banned overclaim words | Stale "In the works" labels undersold the product. Status is told in badge text. |
| Capability spec | Maps each card to real routes through the real router, failing on placeholder steps or a redirect. | — | — | `landing-capability-routes.spec.ts` | The page fails a test before it can overclaim. |
| Tour | `cp-tabs` beside a browser-frame figure. A picture is mounted on its first selection. `#tour-*` anchors. | `cp-tabs` | Crossfade, instant under reduced motion | Tabs, hash and click selection, captures list | It shipped empty until photographed. "Visible, not in the DOM" and `requiresUrl` were both learned in a real run. |
| How it works | Four numbered steps in an `ol`. | Primary soft tokens | Reveal | Lifecycle wording, banned words | The class was renamed `.sequence` to stay out of the part rules. |
| Pricing | Catalogue cards, a monthly or yearly radio group, a comparison table, "Recommended for…" in words plus a 2px edge, `/sign-up?plan=`. | `cp-choice-group`, `cp-card` | Price count, instant under reduced motion | No prices in the bundle, the table semantics | An anonymous GET with a 300 s cache and a gateway rate limit. The flag stays off until legal review. Two animation loops raced. |
| Credits, packs, compare | The credit menu, the estimator, packs, the draft-versus-final band. | — | Count-up | Edge cases | **Dropped here**: PantryPress has no credits or AI. The compare band was built and then gated off because it had nothing true to show. |
| FAQ | Native `details`, with `h3` inside `summary`. Entries tied to the catalogue hide without it. | — | Marker rotation only | Count with and without the catalogue; safety wording | Undecided topics were left out, not guessed. |
| Trust | Mission, audience, stage line, a proof note. | Narrow frame | None, on purpose | No stars, counts, or "trusted" | Real stories are a follow-up. |
| Final CTA, footer | CTAs repeated. The footer sits outside `main` with one `nav aria-label="Legal"`. | — | — | Hrefs, landmark | An empty footer `nav` was removed. |
| Legal pages | Markdown in `docs/legal/` parsed into data (no raw HTML). The `h1` is focused on arrival. | Prose measure | — | Parser and every real file | Reviewer notes and MOCK-UP banners are dropped by the parser. |
| Document head | Static tags in `index.html`, checked as text, with one AVIF preload of the hero photo. | — | — | `landing-document-head.spec.ts` | Tags set by the `Meta` service are invisible to scrapers. **Here prerendering puts them in real HTML.** |
| Reveal directive | Inline styles only, a capped stagger, `beforeprint`, settles after 1.0 s. | Motion tokens | — | Untouched under reduced motion | It is never put on the hero. |
| Navigation bar | Built, then removed by the owner. | — | — | — | It spoiled the hero (decision 7). |
| Page contract | A static lint of public templates and CSS (`public-h1`, `public-frame`, `public-control`) with `node:test` proofs. | Public frame tokens | — | `check-page-contract.test.mjs` | A public page passed only because it was skipped (LP.1 finding 10). |
| Performance | LCP 2.2 s in LP.11; 3.2 s in LP.12 with a recreated harness. | — | — | None committed | **Commit the harness.** |

### Findings in PantryPress

| Area | What exists | Finding |
| --- | --- | --- |
| Stack | Angular 22.2 workspace under `src/web` (`@angular/build:application`, vitest, ESLint with an SSR-safety config, Prettier). `@angular/ssr` is installed for `pantrypress-site`. | Prerendering the admin app's public routes needs no new framework, only configuration (PPL.2). |
| Routing | `pantrypress-admin/src/app/app.routes.ts` and the site's are both `[]`. There are no guards and no sign-in screen; `/` is owned by nothing. | Nothing to weaken yet. PP3.8 adds the guards under `/dashboard` after PPL.2. |
| Admin `index.html` | Title "PantryPress", no description, canonical, Open Graph, or `robots.txt`. | PPL.20. |
| Design system | `@pantrypress/ui` and `/themes` export nothing yet. No tokens or fonts. PP3.1–PP3.6 are planned. | PPL.6 builds the public page's foundation on PP3.2's tokens. |
| Brand | `docs/images/logo.png` and `logo-dark.png`: the robot chef holding a tablet, a teal and orange wordmark. The design mockups use `--f-display` and `--f-body` tokens. | Family branding (decision 5); the logo needs sized renditions (PPL.7). |
| Gateway | Serves `app.pantrypress.app` and `app.localhost` (`Gateway:Hosts`), with the session, antiforgery, CSP, and HSTS. The AppHost rewrites the dashboard URL to `app.localhost`. Account and invitation emails link to `Accounts:AppBaseUrl`. | PPL.1 moves all of it to the apex. About 20 test files name the old host. |
| Shipped capabilities | Accounts (PP1: registration, verification, password, two-factor, BFF sessions); sites, members, and invitations (PP2.10–PP2.11); host resolution and status answers on the public tier (PP2.12–PP2.13). All server-side. | **No blogger-facing screen ships yet.** Almost every claim is **Planned** (decision 4), and PPL.4 re-measures this before each build stage. |
| Plans and Stripe | No Billing module or catalogue (PP14.1); prices live in Stripe (`billing.md`). Drafts are in `docs/pricing/` and `docs/features/plans/`. D-09 and D-10 are open. | PPL.15 waits for PP14.1. |
| Legal | No `docs/legal/`. PP19.13 writes the platform documents. | PPL.19 renders them; until then, "(coming soon)". |
| CreatorPantry link | PantryPress: none (PP18 planned). CreatorPantry at `d72f5cd`: no PantryPress adapter; Buffer is the only publishing provider (`Modules/Publishing/Integration/Buffer/`), and PantryPress appears only in billing docs (`ai-pricing.md` B-44). | Decision 8: a **Coming soon** card only. |
| Tooling | No Playwright, Lighthouse, axe, or sharp in `src/web/package.json`. `anyComponentStyle` budget: 4 kB warning, 8 kB error. | PPL.7, PPL.11, PPL.21, and PPL.22 add what they need as devDependencies and say so. A section's styles stay within the component budget. |
| Toolkit | Skills: `add-admin-component`, `add-endpoint`, `add-aspire-resource`. Reviewers: `design-review`, `architecture-reviewer`, `api-contract-checker`, `site-isolation-auditor`, `test-gap-analyzer`, `public-site-reviewer`. | Used by name below. `public-site-reviewer` is for tenant output; it reviews the apex only for no-JS behaviour, budgets, and headers (PPL.20–PPL.22). |

## Prerequisites already satisfied

- ADR 0019 and the reconciled documents (CLAUDE.md, the rules, the architecture, the requirements, the main prompts),
  2026-10-28.
- The gateway's session, antiforgery, same-origin check, CSP, HSTS, and neutral 404 (PP1.5–PP1.12). Only its host
  changes.
- Registration, verification, and sign-in APIs (PP1.2–PP1.6). PP3.8 builds their screens.
- The public tier's neutral 404 for a host that names no site (PP2.12). The apex is such a host.

## Credentials and external prerequisites

| First needed | Requirement |
| --- | --- |
| PPL.1–PPL.14 | None. Everything runs under `aspire run` at `localhost` and `{slug}.localhost`. |
| PPL.7 | The twelve food photographs copied from CreatorPantry's committed renditions, which the owner owns (decision 13). |
| PPL.15–PPL.16 | None while the Stripe fake answers (PP14.4's fake). Live prices need the PP14 Stripe test account. |
| PPL.18 | The mailbox robert@pantrypress.app exists before it is printed. |
| Going public | PP9 (DNS apex alias, Front Door routes), PP19.13 (legal text reviewed), the BIZ-009 controls, and `robots.txt` opened. |

## Out of bounds

- Checkout on the landing page, a waitlist, or any email capture. Plan CTAs go to sign-up with `?plan=<key>`.
- Third-party analytics, tracking, or embeds (IR-12). Remote fonts.
- AI language or features (PP-D12, IR-13).
- Showcasing real bloggers' sites, a directory of blogs, or "recipes from our community": that is the consumer index
  CLAUDE.md rules out, and it would publish people's work without asking. Only sample sites appear.
- Any code, package, or runtime dependency on CreatorPantry. Its photographs are copied once as assets; its tokens are
  rebuilt here.
- A separate marketing app, a blog, a docs site, or a help centre.
- Fabricated proof of any kind (IR-1).

## When each prompt runs

| Prompt | Runs | Waits for |
| --- | --- | --- |
| PPL.1–PPL.3 | Now, after PP2.13 and before PP3.8 | — |
| PPL.4–PPL.5 | Any time after PPL.3. PPL.4 runs again before PPL.22 and before launch. | — |
| PPL.6 | After PP3.1–PP3.3 | Tokens, buttons, the design-system document |
| PPL.7–PPL.10, PPL.14, PPL.17, PPL.18 | After PPL.6 | PPL.5's approved copy |
| PPL.11 | After PP3.9 | Dashboard screens worth photographing (PP4.12, PP5.11 and on). The capture is re-run as features ship. |
| PPL.12 | After PP7.13a; its gallery after PP11.14 | A blog to photograph; the presets |
| PPL.13 | After PP3.8 | The sign-up screen |
| PPL.15–PPL.16 | After PP14.1 | The catalogue |
| PPL.19 | After PPL.6 | Content from PP19.13 |
| PPL.20 | After PPL.8 | — |
| PPL.21–PPL.22 | After the sections exist; again inside PP19.16 | — |

## Checkpoints

| After | Demonstrable result |
| --- | --- |
| **PPL.3** | `https://localhost:<port>/` serves a prerendered page that reads with JavaScript off. A signed-in session at `/` or `/pricing` gets a 302 to `/dashboard` before any HTML. Account email links open `localhost`. No host but the apex answers the gateway. |
| **PPL.10** | The hero, value props, and how it works render from the approved copy. Every card is either backed by a real route or badged **Coming soon** with its prompt, and a spec fails on either drift. |
| **PPL.16** | Pricing renders from the server's catalogue behind the flag and disappears cleanly without it. `/pricing` opens at its heading. |
| **PPL.22** | The composed page passes its budget with the committed harness, WCAG 2.2 AA in both themes at three widths and 200% zoom, a keyboard-only walk, and a journey through the real gateway. `design-review` passes. |

---

## PPL.1 Move the gateway to the bare address

```text
SCOPE: Move the platform home from app.<zone> to the zone's apex per ADR 0019. The changes:
- Gateway:Hosts becomes pantrypress.app in appsettings.json and localhost in Development.
- The Gateway:PublicOrigin examples and messages change to match.
- The AppHost's local URL rewrite changes from app.localhost to localhost, along with the origin it hands to
  Accounts:AppBaseUrl and SiteInvitationEmailOptions, so account and invitation emails open the apex.
- Every test, comment, and doc string that names app.localhost or app.pantrypress.app is updated.
- The reserved-slug test keeps `app` and `www` reserved, finding them in the Terraform now that the gateway's hosts
  carry no label.
CONSTRAINT: PP-D19 (ADR 0019); gateway.md, auth.md, aspire.md, sites.md, tenancy.md; add-aspire-resource.
RESTRICTION: The host is the only thing that changes. The cookies, antiforgery, the same-origin check, CORS, the
CSP, HSTS, the trusted-proxy step, and the neutral 404 for any other host all stay exactly as they are. The Site host
still answers the apex (`localhost` locally) with the neutral 404; prove it by adding the bare zone to
SiteHostTests' tricks. `localhost` cookies are not isolated by port, so assert that the Site host still registers
no cookie handling and reads none (public-endpoints.md). No Terraform here: PP9.1a and PP9.2 own the apex records
and routes. No migration.
UTILIZATION: architecture-reviewer and site-isolation-auditor after implementation.
BEHAVIOR: Grep every reference first. Show the file list and the new local URLs (the dashboard link in the Aspire
dashboard, email links). Wait for approval. Implement, then run dotnet build, dotnet test, and dotnet format
--verify-no-changes. Sign up and sign in at https://localhost:<port> under aspire run. Report.
```

**Delivery note (2026-10-29).**

- **What changed: the host alone.**
  - `Gateway:Hosts` is `pantrypress.app` in `appsettings.json` and `localhost` in Development.
  - The AppHost names `localhost` for the gateway's dashboard link and hands `https://localhost:{gateway https port}`
    to `Accounts:AppBaseUrl`, so account and invitation emails open the local apex.
  - Every test, comment, and message that named `app.localhost` or `app.pantrypress.app` as the platform home now
    names the apex. Where they remain, it is as hosts that are refused.
  - The cookies, antiforgery, the same-origin check, CORS, the CSP, HSTS, the trusted-proxy step, and the neutral 404
    are unchanged.
- **New proof.**
  - The gateway answers `app.` and `www.` under either zone with its neutral 404, both under test and on the real
    gateway (`EdgeHardeningTests`, `EdgeHardeningEndToEndTests`).
  - A write from a blog's origin is refused (`GatewayCorsTests`). Since PP-D19 a blog is same-site with the apex,
    though never same-origin.
  - `HostTricks` gains `www.<zone>` and the bare `localhost`.
  - `SiteHostTests` runs the platform home's hosts under a run whose zone really is `localhost`.
  - The gateway's session and antiforgery cookies are sent to the Site host at `localhost` and on a site's own host,
    to the probe and to `/api/public/v1/site`. They change no status, body, or cache header, add no
    `Vary: Cookie`, and set nothing.
- **Differs from the prompt.**
  - The reserved-slug test cannot find `www` in the Terraform, which names only `app` (as `admin_host`) until PP9.1a.
    So it pins `app` and `www` as `PlatformHost` by hand, and checks that every gateway host is the zone's apex, which
    has no label to reserve. When PP9.1a adds `www`, the Terraform scan's known set should gain it.
  - The rule text for `tenancy.md`'s host tricks and `aspire.md`'s local hosts was brought up to date.
- **Open, for later prompts.**
  - **Deployed hosts still answer on `app.<zone>`** until PP9.1a and PP9.2. The Terraform's `admin_host` sets
    `Gateway__Hosts__0`, `Gateway__PublicOrigin`, and `Accounts__AppBaseUrl` over `appsettings.json`.
  - **PP17.2 owes a check in the schema.** Today only site creation writes a `SiteHost`. Custom domains must refuse
    the apex, `app.`/`www.<zone>`, and any deeper name under the zone. Nothing below the facade refuses them yet.
  - **Cookie flooding (record in ADR 0019 or with PP9.1a).** A third-party script on a blog can set
    `Domain=<zone>` cookies that reach the apex. They cannot overwrite the `__Host-` cookies, but enough of them
    could make the dashboard's requests too large (431). Putting the zone on the Public Suffix List would close this.
  - **Locally, `localhost` cookies are shared by port.** The session cookie also reaches Mailpit, DbGate, Redis
    Insight, the Aspire dashboard, and ApiService's direct port on this machine. Only the Site host was checked.
- **Verification.**
  - `dotnet build` and `dotnet format --verify-no-changes`, both clean.
  - `dotnet test`: 1,804 of 1,804. This includes the AppHost tests, which sign up, confirm by email, and sign in
    through the real gateway at `https://localhost:<port>`.
  - Reviews: `architecture-reviewer` and `site-isolation-auditor` found no blockers. Their test gaps were added
    above, and the rest are under "Open".

## PPL.2 The platform home's route table and prerendering - done

```text
SCOPE: In pantrypress-admin, write the route table for the apex:
- '' renders the landing page (an empty LandingComponent shell for now: <main> with the product name as its h1);
- 'pricing' renders the same component with data { title: 'Pricing', scrollTo: 'pricing' };
- the legal paths render a placeholder until PPL.19;
- 'sign-in', 'sign-up', 'verify-email' and 'reset-password' are reserved for PP3.8;
- 'dashboard' is the root of everything signed in;
- anything else gets a not-found page.
Export the public paths as one constant (PUBLIC_PATHS) so PPL.3 and the prerender configuration read the same list.
Configure the build to prerender PUBLIC_PATHS and render every other route in the browser, with no server output.
CONSTRAINT: PP-D19, PP-D08; frontend.md; Angular 22's application builder. Verify the current outputMode and
server-routes API rather than assuming it.
RESTRICTION: A prerendered route reads no session, account, site, or price, and makes no HTTP call while it is
prerendered. The build emits static files only; no Node server at runtime. Nothing under 'dashboard' is prerendered.
No guard is added here (PP3.8). The component style budget stays.
UTILIZATION: architecture-reviewer after implementation.
BEHAVIOR: Show the route table and the build's output layout (which file the gateway serves for '/', '/pricing',
'/terms', '/dashboard/x', and '/nope'). Wait for approval. Implement with RouterTestingHarness specs. Run npm run build
and show that the prerendered '/' contains the h1 with JavaScript disabled. Report.
```

**Delivery note (2026-10-29).**

- **What shipped.**
  - `pantrypress-admin` builds with `outputMode: "static"` and a `server` entry (Angular 22.2.4's application builder;
    `@angular/ssr`'s `withRoutes`). `app.routes.server.ts` prerenders exactly `PUBLIC_PATHS` and sends everything
    else (`'**'`) to `RenderMode.Client`. No route renders on a server, and the build emits no server.
  - Build output (`dist/pantrypress-admin/browser/`): `index.html` is `/`, `pricing/index.html`, and
    `terms|privacy|acceptable-use|dmca/index.html`. `index.csr.html` is the browser-rendered document for every other
    path: `/dashboard/x` and `/nope` both draw the not-found page from it. **PPL.3 must fall back to `index.csr.html`,
    never `index.html`**, which is now the landing page.
  - The route table:
    - `/` and `/pricing` share the eager `Landing` shell; `/pricing` carries `data { title, scrollTo }`.
    - The four legal paths render a placeholder `h1` until PPL.19.
    - The reserved auth paths render the not-found page until PP3.8.
    - `/dashboard` lazy-loads `dashboard.routes.ts`, a placeholder with no guard.
    - `'**'` is the not-found page.
  - The app shell is a bare router outlet. Each page owns its `<main>` and its one `h1`, and the showcase is now a
    `<main>`.
  - Hydration is on (`provideClientHydration(withEventReplay())`): the prerendered page is taken over where it
    stands, not drawn again. With hydration off, the prerendered `h1` was torn down on bootstrap; with it on, it never
    was.
- **Differs from the prompt.**
  - `forgot-password` is reserved as well, because `frontend.md` lists it among the client-rendered routes.
  - Reserved paths render the not-found page rather than a placeholder, because nothing is there yet.
  - **The redirect list is not `PUBLIC_PATHS`.** As first written, PPL.3's drift test would have redirected a signed-in
    owner away from `/terms`, against ADR 0019, which redirects only `/` and `/pricing`. `public-paths.ts` now also
    exports `SIGNED_IN_REDIRECT_PATHS = ['', 'pricing']`. PPL.3's text and `frontend.md` were reconciled to name it.
  - The no-HTTP rule has two parts:
    - `provideApi()` refuses every request on the server (`refuse-on-server.ts`, a plain error that `ApiClient`
      rethrows rather than drawing it as a refusal).
    - The route spec renders every public path on the server platform with a recording `ApiClient` and asserts no
      call. A browser-side control shows the recorder does see calls.
  - Added after `architecture-reviewer`:
    - The not-found page's home link is a plain `href`, because a `routerLink` to `/` would skip the gateway's
      signed-in redirect. `frontend.md` now says so for `/` and `/pricing`.
    - The SSR-safety lint (`eslint.ssr-safety.js`) now covers the admin app's prerendered code (`src/app/*.ts`,
      `core/**`, landing, legal, not-found, `main.server.ts`), with the HTTP net carried into that block.
    - A spec fails on a top-level route that is neither in `PUBLIC_PATHS` nor browser-only.
- **Open, for later prompts.**
  - **PPL.3:** the build's critical-CSS inlining (Beasties) writes an inline `<script>` into every document, which the
    gateway's `script-src 'self'` will block, leaving `styles-*.css` as `media="print"`. Either turn off
    `optimization.styles.inlineCritical` for the admin build or admit the script by its `sha256`; never
    `unsafe-inline`.
  - **PPL.3:** unknown paths are a 200 with the not-found page (a soft 404). Static output cannot say otherwise, and the
    gateway cannot know the dashboard's child routes. PPL.20 should give the not-found page `noindex`.
  - **PPL.3:** nothing automated pins the build layout yet. The gateway's tests should assert one `index.html` per
    public path, plus `index.csr.html`.
  - **PPL.3:** its C# test should read `public-paths.ts` from `export const … = [` to the matching `]` across lines,
    because Prettier wraps a longer list.
  - **PP3.8:** the guard returns a `UrlTree` to `/sign-in`. A `canMatch` that answers `false` would fall through to the
    not-found page. The reserved list is written twice (`app.routes.ts` and its spec), and both go when the screens
    arrive.
  - **PP3.9:** `dashboard.routes.ts` has only `''`, so `/dashboard/join` and `/dashboard/account/security` (the links
    email sends) fall to the top-level not-found page until the shell and its routes exist.
  - **Under `aspire run`:** `ng serve` now renders the public paths per request. A direct run served `/`, `/pricing`,
    and `/terms` with their `h1` and the other paths as the browser document. Restart the `admin` resource to pick up
    the `angular.json` change; it was not re-checked through the gateway's catch-all.
- **Verification.**
  - `npm test`: every suite passes, the admin app's 75 tests among them.
  - `npm run lint` and `npm run format:check`: clean.
  - `npm run build`: "Prerendered 6 static routes", with no server output.
  - The showcase e2e passes on Chromium: 25 of 25.
  - Chromium with JavaScript disabled shows the `h1` in the prerendered `/`, `/pricing`, and `/terms`.
  - `architecture-reviewer`: no blockers. Its four should-fixes are resolved above, and its notes are under "Open".

## PPL.3 Gateway: prerendered pages, the signed-in redirect, and document headers - done

```text
SCOPE: Make the gateway serve pantrypress-admin's build output:
- the prerendered document for each public path, and the browser-rendered document for every other non-/api path;
- Development keeps its dev-server catch-all.
A GET or HEAD of '/' or '/pricing' that carries a complete session ticket gets a 302 to '/dashboard' before any
document is read. Headers:
- Cache-Control: no-cache on every document;
- long-lived immutable caching on fingerprinted assets only.
The gateway's list of redirected paths lives in configuration. A test reads PPL.2's public-paths.ts as text and
fails if the list drifts from SIGNED_IN_REDIRECT_PATHS (the legal pages are public but never redirected), or if the
documents the gateway serves drift from PUBLIC_PATHS.
CONSTRAINT: PP-D19 (ADR 0019); gateway.md, auth.md; the PSEC-002 headers.
RESTRICTION:
- The redirect reads only the gateway's own cookie and asks ApiService nothing.
- A half-finished ticket is not a session.
- A revoked session that still holds a cookie is redirected, and the dashboard's guard then meets `session-ended`,
  clears the cookie, and goes to /sign-in. Prove that this cannot loop.
- Every document carries the PSEC-002 headers and the admin app's CSP, which admits self-hosted fonts only.
- No /api route changes: an unknown /api path is still the gateway's problem 404, and an unknown page path is the
  app's not-found page.
- No host other than Gateway:Hosts is answered.
UTILIZATION: architecture-reviewer and test-gap-analyzer after implementation.
BEHAVIOR: Show the request table: anonymous, half-finished, complete, and revoked, against '/', '/pricing', '/terms',
'/dashboard/x', '/nope', and '/api/nope', giving the status, Location, and Cache-Control for each. Wait for approval.
Implement with gateway tests through the real pipeline. Report.
```

**Delivery note (2026-10-29).**

- **What shipped.**
  - **Serving the build.** Outside Development the gateway serves the admin app's build from `Gateway:AdminApp:Root`
    (`AdminApp.cs`), as one `GET`/`HEAD` route at the lowest order:
    - a public path gets its prerendered document;
    - every other page path gets `index.csr.html`. That covers everything under `/dashboard`, a last segment with no
      dot, and any `.html` name, so `/index.html` cannot step around the redirect;
    - anything else is a file of the build or the gateway's problem 404. A stale chunk is never answered with HTML;
    - `/api` (in any case) and the probe paths are never pages.
  - **The signed-in redirect.** It runs after `UseAuthentication` in every environment, ahead of the dev server too.
    `GET`/`HEAD` of `/` or `/pricing` with a complete session is answered 302 `/dashboard`, `no-store`, from the
    gateway's own cookie alone. A public path spelled as the router would also read it (`/pricing/`, `/pricing;x=1`,
    `//`) is first answered 301 to its own spelling.
  - **Caching.**
    - Documents are `no-cache`, and their tag is their content's hash.
    - Angular's bundles and `media/` files are immutable for a year. Anything copied from `public/` is `no-cache`.
    - Any page or file answer that sets or clears a cookie is `no-cache, no-store`.
    - The dev server's HTML is made `no-cache` on the way through.
  - **Configuration.** `Gateway:AdminApp` holds `PublicPaths`, `SignedInRedirectPaths`, `SignedInHome`, and `Root`
    (default `admin`, under the content root).
  - **Startup refusals.** The host refuses to start on:
    - a list that could loop, or a malformed path;
    - a missing build, document, or `index.csr.html`;
    - a document with inline script or an inline event handler;
    - `Root` set in Development, or the `admin-spa` route beside a build.
  - **CSP: option 1, as approved.** The document policy is unchanged (`script-src 'self'`, `font-src 'self'`). The admin
    production build turns off critical-CSS inlining, and hydration runs without event replay. `npm run build` now runs
    `check:admin-build`, which applies the gateway's rule to the real output and fails the build on a breach.
- **Verified in a browser.** The real gateway ran in Production mode against the real build, in Chromium with
  JavaScript on:
  - no CSP violations;
  - the stylesheet applied;
  - hydration tore nothing down;
  - every Cache-Control as in the table.
- **Differs from the prompt.**
  - **The drift test reads two lists.** It compares the redirected paths with `SIGNED_IN_REDIRECT_PATHS` and the
    served documents with `PUBLIC_PATHS` (the prompt text was reconciled at PPL.2).
  - **Rules added after review.** The 301 for other spellings, dotted dashboard paths as pages, the probe paths as 404,
    content-hash document tags, and the cookie rule were not in the approved table. They came from review.
  - **A real bug the review's tests found.** A session past its 12-hour limit, signed out on a request for a
    fingerprinted file, was answered `public, immutable` with the clearing `Set-Cookie`. Fixed by the cookie rule above.
  - **A PPL.2 test, fixed here.** PPL.2 left `AdminAppTests.Gateway_serves_the_admin_app...` failing: the dev server
    now renders `/`, so `<ppa-root>` has attributes. It now asserts the rendered `h1` and `no-cache` through the real
    gateway. PPL.2's run had covered only the web suites.
- **Open, for later prompts.**
  - **PP19.2 and PP19.4 (now written into both prompts).** A gateway image without the build at `/app/admin` refuses to
    start. Until PP19.2 maps them, `/health` and `/alive` are the gateway's 404 outside Development.
  - **PP3.8.** The guard turns `session-ended` into `/sign-in`, and never sends a 502 or a timeout to `/` or `/pricing`
    by a full page load, or "API down" becomes a 302 loop.
  - **Unknown page paths are a 200.** The app's not-found page is drawn by the browser; PPL.20 should give it `noindex`.
  - **Later PPL prompts (no event replay, no critical-CSS inlining).** A click before hydration is lost, so PPL.8,
    PPL.13, and PPL.16 controls work as links or forms first. The stylesheet blocks rendering; PPL.21 measures LCP with
    that in place.
  - **`style-src 'unsafe-inline'` stays** for Angular's inline component styles. Static files cannot carry a nonce;
    per-document hashes could replace it later.
  - **No rate limit on pages or files at the gateway.** The apex is left to the PP9.2 WAF. The redirect is not under
    the session limiter.
  - **Test fixture builds are not deleted.** Each test run leaves a few small folders under `%TEMP%`.
- **Verification.**
  - `dotnet build`: clean. `dotnet test`: 1,989 of 1,989. `dotnet format --verify-no-changes`: clean.
  - `npm test`, `npm run lint`, `npm run format:check`, and `npm run build` (with `check:admin-build`): all clean. The
    check was also shown to fail on a planted inline script.
  - The showcase e2e: 25 of 25 on Chromium.
  - Reviews:
    - `architecture-reviewer`: no blockers. Its three should-fixes are resolved, with one change of approach: the
      fingerprint rule keys on Angular's own names, because its real chunk hashes are mixed case.
    - `test-gap-analyzer`: its major gaps are covered. Those tests are the request table cell by cell; tampered,
      renamed, swapped, and foreign-key-ring tickets; revalidation after sign-in; `Set-Cookie` on cacheable answers;
      probes; traversal against a real file beside the build; and the drift parser's self-test. Its minor items on
      rate limits and trusted proxies on page paths are recorded, not tested.

## PPL.4 Capability review (report only)

```text
SCOPE: Review what PantryPress actually offers a blogger today and return a findings report.
- For every item on the requirements' food-blogger checklist (section 2.4), and for BIZ-002, BIZ-003, BIZ-006,
  BIZ-010, and BIZ-011, give its status (Shipped with the file and test that prove it, Planned with the prompt, or
  Not built), and mark anything that exists only as a placeholder or behind a flag.
- Report the CreatorPantry link from both repositories, the plan catalogue, the legal pages, the sign-up gate, and
  every dashboard route that exists.
- End with "what the page may claim today".
CONSTRAINT: PPL-002; CLAUDE.md Scope; the requirements; the main prompts' done markers.
RESTRICTION: Report first; change nothing. Read CreatorPantry read-only and from its code, not its docs. A prompt marked
done whose verification did not pass is reported, not counted.
BEHAVIOR: Present the table ranked by what the page most wants to say, and wait for approval before PPL.5. Run this
again before PPL.22 and before launch, and diff the result against the previous run.
```

## PPL.5 Narrative and copy document

```text
SCOPE: Write docs/landing-page-copy.md section by section, in the shape of CreatorPantry's landing-page-copy.md:
hero, value props, product tour, "Your blog, as readers see it", how it works, preset gallery, "Your content stays
yours", pricing, FAQ, trust, final CTA, footer, and legal links. For each section give:
- the headline, the body, and the CTAs;
- every claim with its tag (Shipped, Decided, Planned (PPx.y), Catalogue, or Not built) and its source;
- the motion intent.
End with "Owner answers" (decisions 1–14 above, dated) and "Still open".
CONSTRAINT: PPL-002, PPL-007; decisions 4, 5, 8, 9, 10, 11, and 12; CLAUDE.md Scope and its plain-language rule;
PPL.4's report; requirements 1.1 (the one sentence) and 2.1 (the personas).
RESTRICTION: Bound by IR-1, IR-2, IR-3, IR-13, and IR-14. Prices are named as catalogue fields. A Planned claim names
its prompt. No claim about speed, SEO, or earnings beyond what a requirement promises and a prompt verifies (BIZ-005,
RND-007); "fast" is allowed, a number is not. The robot is never the subject of a verb that does a blogger's work.
BEHAVIOR: Present the document with every claim tagged, and wait for approval. No components in this prompt.
```

## PPL.6 The public page's design foundation

```text
SCOPE: Build the foundation the landing page draws on, in @pantrypress/ui:
- Tokens in light and dark: the logo-led palette (teal, orange, green for actions), paper and ink surfaces, and
  hairlines.
- Public frame tokens: gutter, section spacing, and narrow, normal, and wide widths, plus a prose measure.
- A display type size (fluid clamp) and a single public body size.
- Self-hosted Fredoka (500, 600, 700) and DM Sans (400–700) as subset woff2 with font-display swap, their licences
  committed, and explicit loaded weights.
- A focus ring at least 3:1 against every surface it can sit on, in both themes.
Fill "The public page" in src/web/PANTRYPRESS-DESIGN-SYSTEM.md with what a public page may draw itself (the hero, its
sections, its frame from the public tokens) and what it must take from the library.
Add scripts/check-public-page.mjs, with node:test proofs that each rule fires:
- one h1, in the hero or a legal page only;
- frames from the public tokens;
- every <button> is a library button;
- no literal colour.
Run it from npm test.
CONSTRAINT: PPL-004, PPL-011; decision 5; design-system.md (both its public-page and shared-token sections);
frontend.md; ADR 0007; PP3.2's tokens and PP3.3's buttons (stop if they are absent).
RESTRICTION: Nothing imported or copied as code from @creator-pantry/ui; CreatorPantry's rules are the reference, and
these are written fresh. Tokens only, never a literal colour in a component. No remote font, no font-loading script.
The ring is measured, not eyeballed: a spec computes its contrast against every surface token in both themes.
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the token table with measured contrast ratios, the type scale at 375 and 1440 px, and the font files and
their sizes. Wait for approval. Implement with specs. Report.
```

## PPL.7 Brand and food images

```text
SCOPE: Prepare the page's images.
- The logo: theme-specific WebP renditions near their shown sizes (200 and 400 px wide), with intrinsic width and
  height, made from docs/images/logo.png and logo-dark.png.
- The food photographs: CreatorPantry's twelve committed renditions (AVIF and WebP at 480 and 960 px, plus a 480 px
  JPEG fallback) copied into pantrypress-admin's public/images/food/, with a PROVENANCE.md naming CreatorPantry's
  commit and the owner's permission (decision 13).
- npm run make:images (sharp as a new devDependency) regenerates both from a local originals folder given by
  IMAGE_SOURCE_DIR.
CONSTRAINT: PPL-004, PPL-010; decisions 5 and 13; IR-6.
RESTRICTION: Copy image files only. No script, build step, or runtime reads the CreatorPantry repository, so there is
no dependency on it. No 1600 px rendition unless a layout shows a photo wider than 480 CSS px. No photo shows a person
or a real blog.
BEHAVIOR: Show the file list with byte sizes, and a contact sheet of the twelve photos. Wait for approval. Implement and
report the total bytes added.
```

## PPL.8 Hero

```text
SCOPE: Add LandingHeroComponent and LandingHeroCollageComponent.
- The hero holds the logo (fetchpriority high, intrinsic size), the h1 at the display size with the one phrase PPL.5
  chose highlighted by a hand-drawn underline painted as a background, the subheadline, and two CTAs above the fold at
  375 px. The CTAs are the sign-up CTA from PPL.13 (a plain link to /sign-up until PPL.13 lands) and "Sign in" →
  /sign-in.
- The collage holds three fixed food photos (HERO_COLLAGE_PHOTOS) at slight rotations with soft shadows, in reserved
  aspect ratios.
- The front photo is preloaded in the prerendered head.
CONSTRAINT: PPL-004, PPL-010, PPL-011; PPL.5's hero copy; PPL.6's tokens; add-admin-component; IR-5, IR-6, IR-7, IR-9,
IR-13.
RESTRICTION: Tokens only. The highlight is a background: a pseudo-element or mask makes its own paint layer and lets a
photo become the LCP candidate. No layout shift. Parallax drift is written to CSS variables without change detection,
only under no-preference reduced motion with a fine pointer, and pauses off-screen. The hero is never revealed or
animated in. The collage is aria-hidden with empty alt text. No AI proposal card or any other mocked UI.
UTILIZATION: design-review after implementation; Playwright screenshots at 375, 768, and 1440 px in both themes.
BEHAVIOR: Show a static mock (screenshots) and the motion spec. Wait for approval. Implement with component specs and
axe. Report.
```

## PPL.9 Value props and the capability spec

```text
SCOPE: Add LandingValuePropsComponent:
- PPL.5's cards in a library card grid, each with a badge, an h3, and a body;
- a planned card carries the badge text "Coming soon";
- a card links to its tour stop only when that stop has been captured (PPL.11).
Add landing-capability-routes.spec.ts. It maps every card to its evidence and fails when:
- a card without "Coming soon" has no dashboard route that resolves through the real router to a real component,
  where a redirect, a placeholder, or a missing leaf all count as failure;
- a "Coming soon" card names a prompt that the main prompts or this document already mark done, which means the page
  now undersells (read the prompt documents as text);
- a "Coming soon" card names a prompt that does not exist;
- a mapping is stale.
CONSTRAINT: PPL-002; PPL.4's report; PPL.5's cards; IR-2, IR-7, IR-9, IR-13.
RESTRICTION: Status is told in words, not tone. The spec tests what exists, not who may see it: guards are stripped
for the route check, as CreatorPantry's was. The CreatorPantry card stays "Coming soon" until PP18 is done and
CreatorPantry's adapter exists (decision 8). The latter is checked by the owner, not by reading CreatorPantry's code
from a test.
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the card-to-evidence table. Wait for approval. Implement with specs. Report.
```

## PPL.10 How it works

```text
SCOPE: Add LandingHowItWorksComponent: PPL.5's ordered steps mirroring PantryPress's real lifecycle:
- sign up and launch a blog with "Launch my blog" (PP10.9);
- write a post with a recipe card (PP5, PP4);
- preview and publish, which updates the live blog (PP5.14);
- readers find it through search, Pinterest, and the blog's own feed (PP8);
- readers rate, comment, and subscribe (PP12, PP13).
Steps not yet shipped carry "Coming soon".
CONSTRAINT: PPL-002; content.md (publishing is one confirmed action, a snapshot), recipes.md (wording preserved,
deterministic scaling), rendering.md; PPL.5's copy; IR-2, IR-13.
RESTRICTION: No step that does not exist and no shortcut narrative. "Publish" means it is on the blog now (the "easy as
pie" rule). Scaling and conversion are described as exact arithmetic, never "smart". CreatorPantry appears here only
once the link works end to end (decision 8).
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the step-to-lifecycle mapping. Wait for approval. Implement with specs. Report.
```

## PPL.11 Product tour and the capture script

```text
SCOPE: Add LandingTourComponent and scripts/capture-tour.mjs.
The component:
- WAI-ARIA tabs of dashboard stops (for example: the post editor, the recipe editor, publishing, the Studio
  customizer, members), each with a promise, three benefits, and a screenshot in a frame beside the panels;
- a picture is mounted on its tab's first selection;
- #tour-* anchors that select and focus their tab.
The capture script:
- signs in as tour-capture@pantrypress.local against a running aspire run at https://localhost:<port>;
- refuses any site outside SAMPLE_SITES;
- requires each stop's proof text to be visible, scrolled into view first;
- requires a captured URL for any stop that lives at an id (requiresUrl);
- photographs 1280×800 at deviceScaleFactor 2 in both themes with reduced motion;
- writes AVIF and WebP at 1x and 2x;
- rewrites a generated landing-tour.captures.ts;
- fails by name on any stop it cannot honestly photograph.
CONSTRAINT: PPL-003; PPL.5's tour copy; frontend.md; IR-4, IR-5, IR-7; Playwright as a new devDependency.
RESTRICTION:
- The tour renders nothing until at least one stop is captured.
- Screens show seeded sample data only, never a real blogger's.
- The script's drift checks between its stops and the component's fail loudly: parse the exported list, not a marker
  comment.
- A stop whose screen does not exist yet is not in the tour. It is a value-prop card marked "Coming soon".
- The crossfade is instant under reduced motion.
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the stop list, the sample data to seed, and the capture plan. Wait for approval. Implement the script,
component, and specs. Capture what exists today. Report which stops are captured and which were refused, and why.
```

## PPL.12 "Your blog, as readers see it" and the preset gallery

```text
SCOPE: Extend the capture script to photograph the sample blogs at {slug}.localhost, then add two sections.
- The reader view: one sample post at 390×844 in a phone frame, at three stops (the post's top, jump to recipe, and
  the recipe card scaled to 2× with metric units), with a sentence each.
- The preset gallery: the sample blog's home in each preset, named, as a scrollable row with keyboard support.
Captures come only from sites in SAMPLE_SITES; one sample site per preset is acceptable.
CONSTRAINT: PPL-003; rendering.md and recipes.md (what the reader view may claim); sites.md and customization-spec
presets (PP11.14); PPL.5's copy; IR-4, IR-5, IR-7.
RESTRICTION: Each section renders nothing until it has captures. A preset's plan is never stated here: which plans
include which presets comes from the catalogue or goes unsaid. The phone frame is decorative and aria-hidden; the words
carry the meaning. Never photograph a real blog (Out of bounds). Scaling is described as exact, not "smart" (IR-13).
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the shot list and frames. Wait for approval. Implement with specs. The gallery half waits for PP11.14
and ships empty until then. Report.
```

## PPL.13 The sign-up gate

```text
SCOPE: Make sign-up honest before launch.
- Add Accounts:PublicSignUp: off in appsettings.json, on in run mode through the AppHost.
- With it off, the registration endpoint refuses with 403 `sign-up-closed`, a stable problem code with a plain
  title.
- The platform home learns the flag through the admin app's runtime configuration (PP3.7).
- The hero's and final CTA's primary button is either "Start your blog" → /sign-up (with ?plan= when chosen) or
  "Sign-up opens at launch" (not a link). The prerendered HTML ships the closed state, and both states take the same
  box, so switching causes no layout shift.
CONSTRAINT: PPL-012; BIZ-009; api-contract.md (a new problem code, documented in OpenAPI); auth.md; aspire.md;
add-endpoint for the refusal; add-admin-component for the CTA.
RESTRICTION: The server is the gate. The button is courtesy and never the only control. Sign-in, verification, and
password reset are untouched. The flag may be turned on in a deployment only after PP12.3, PP19.7, and PP19.9 are done;
record that in the flag's configuration comment and in the launch gate. No waitlist and no email capture.
UTILIZATION: api-contract-checker and design-review after implementation.
BEHAVIOR: Show the flag's states, the problem document, and the CTA's two states. Wait for approval. Implement with API
and component tests. Report.
```

*Owed to PPL.13 by PP3.7 (owner-approved: the runtime configuration is a server read, not a deployed file):*

- **The route.** The admin app already reads `GET /api/v1/client-configuration` (`RUNTIME_CONFIG_PATH`) once per
  browser start, and keeps its defaults on the 404 it gets until this prompt. Add it on ApiService's admin v1 surface
  as an `[AllowAnonymous]` read, through Controller → Facade, with a response record in a module's `Contracts/`
  (`publicSignUp`, camelCase). It needs no gateway route: `api-v1` forwards it already.
- **What it may say.** One answer for every caller: no session, account, site, or price (frontend.md). Its
  `Cache-Control` is chosen on purpose. Fields are additive only, and each one's default in `readRuntimeConfig` is its
  closed or hidden state.
- **Its limit.** It is read on every page load, the prerendered landing page included, so it gets a per-address limit of
  its own. It never shares `account-anonymous` with sign-up and password reset, so page loads cannot crowd those out
  (the gateway.md session-check rule).
- **Its pins.** Add it to `ApiServiceHostTests`' pinned admin paths, write a contract test for its statuses, name it in
  `api-contract.md`'s first bullet beside `/api/v1/plans`, and add a decision-log entry.
- **The CTA.** Draw it from `config()` alone, never from `status`, which stays `loading` on the server. Keep it in the
  eagerly hydrated tree (or switch after `afterNextRender`), so the first browser render matches the prerendered
  closed state. A spec should prove they match.

## PPL.14 "Your content stays yours"

```text
SCOPE: Add LandingPromisesComponent: three tiles from PPL.5.
- Your content, always yours to export (BIZ-006, PP19.11a).
- A downgrade never deletes anything: over-limit items become read-only or hidden with a plain explanation
  (BILL-005, PP14.11).
- No plugins, no updates, no servers to look after (BIZ-003, the closed set).
Each tile carries "Coming soon" until its prompt is done.
CONSTRAINT: PPL-002; billing.md; the requirements; PPL.5; IR-2.
RESTRICTION: A tile states only what a requirement promises and a prompt ships. No refund, uptime, or support-time
promise. The capability spec (PPL.9) covers these tiles too.
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the tiles with their sources. Wait for approval. Implement with specs. Report.
```

## PPL.15 The public plan catalogue route

```text
SCOPE: Add an anonymous, read-only GET /api/v1/plans on ApiService through the gateway. It returns the published
catalogue from PP14.1:
- each plan's key, name, monthly and annual price, the annual saving as a figure, currency, the limits a plan card
  shows, an optional trial length, and which plan is recommended;
- `Cache-Control: public, max-age=300`;
- a per-address rate limit of its own at the gateway.
Billing:PublishPlans decides whether anything is published. Unpublished is a 200 with empty lists, because it is a
state, not a missing resource. The AppHost sets the flag from Landing:Pricing in run mode only, off by default in every
deployment.
CONSTRAINT: PPL-005; billing.md (prices live in Stripe, read through the Billing gateway with a cache; limits live in
configuration); api-contract.md (a declared surface, kebab-case enums, an OpenAPI snapshot); gateway.md; add-endpoint;
docs/features/plans/ for the field set.
RESTRICTION:
- Catalogue data only: no account, site, or usage.
- One of the two anonymous routes outside the account routes, beside PPL.13's `GET /api/v1/client-configuration`
  (PP3.7), and it says why in its doc comment.
- No price literal anywhere in code.
- Startup refuses a published catalogue that is incomplete: a missing price or currency, or a plan key that is not
  URL-safe.
- Landing:Pricing is not turned on outside run mode until PPL.19's legal pages carry reviewed text (IR-10).
- An operator can tell "unpublished" from "Stripe unreachable" in the logs, even though the visitor cannot. This
  closes the gap CreatorPantry left open.
UTILIZATION: api-contract-checker after implementation.
BEHAVIOR: Show the route, the response record, and the flag wiring. Wait for approval. Implement with endpoint tests
and an additive OpenAPI diff. Report.
```

## PPL.16 Pricing section and the /pricing arrival

```text
SCOPE: Add the pricing section and the /pricing arrival.
PlanCatalogueService fetches /api/v1/plans once per page with a shared replay, and no credentials. Every failure
becomes an empty catalogue.
LandingPricingComponent:
- one card per plan;
- a monthly or yearly radio group, named by the question printed above it, with the price counting to its new value;
- "Billed yearly at …" with the saving;
- "Recommended for most food bloggers" on the flagged plan, in words plus elevation;
- a "Compare every plan" disclosure over a real table;
- each CTA is a routerLink to /sign-up?plan=<key> (or the closed state from PPL.13).
/pricing waits for the section, then for document.fonts.ready, scrolls without animation, and focuses the section's
h2. It does nothing when the section did not render.
CONSTRAINT: PPL-005; decision 9; frontend.md; IR-3, IR-5, IR-7, IR-8; PPL.5's pricing copy.
RESTRICTION:
- No figure in the bundle or the prerendered HTML.
- The section renders nothing when the catalogue is empty or down. The hairline between sections does not draw for
  an empty one.
- One animation loop at a time.
- The table sits in a named, focusable region.
- No "most popular". No checkout.
UTILIZATION: design-review and test-gap-analyzer after implementation.
BEHAVIOR: Show the view model, the card and table layout, and the arrival sequence. Wait for approval. Implement with
specs, including a catalogue-down case and a single-request assertion. Report.
```

## PPL.17 FAQ

```text
SCOPE: Add LandingFaqComponent: native details and summary, each question an h3 under the section's h2, all closed. The
questions are PPL.5's answerable ones, for example:
- "Do I need CreatorPantry?" No (BIZ-010).
- "Does PantryPress use AI?" No (PP-D12).
- "Can I use my own domain?" On paid plans, catalogue-gated.
- "Can I leave and take my blog with me?" Export, BIZ-006.
- "What happens if I downgrade?" BILL-005.
- "Can I move from WordPress?" Coming soon, PP20.
- "Is the recipe card good for search?" Recipe structured data, BIZ-005.
Entries that need the catalogue hide without it.
CONSTRAINT: PPL-006; the decision record; IR-2, IR-7, IR-9, IR-13.
RESTRICTION: Anything undecided is left out, not guessed: refunds, cancellation timing, the trial before D-10 closes, and
anything about allergens or nutrition beyond "nutrition is what you enter; PantryPress never estimates it" (RCP-006).
No legal promise.
UTILIZATION: design-review after implementation.
BEHAVIOR: Present the questions with answers and sources. Wait for approval. Implement with specs. Report.
```

## PPL.18 Trust, final CTA, and footer

```text
SCOPE: Add three components.
- LandingTrustComponent: mission from requirements 1.1, who it is for (hobby to prosumer food bloggers), the stage line
  (decision 11), and the proof note (IR-1).
- LandingFinalCtaComponent: PPL.5's line and the same two CTAs.
- LandingFooterComponent, outside <main>:
  - nav aria-label="Legal", with links from PPL.19 or plain "(coming soon)" text;
  - the contact address robert@pantrypress.app;
  - "From the makers of CreatorPantry" → https://creatorpantry.com;
  - "© {year} PantryPress".
CONSTRAINT: PPL-007, PPL-008; decisions 8 and 11; IR-1, IR-7, IR-10.
RESTRICTION: No stars, counts, logos, or "trusted by". The trust section does not move. An empty nav is never
rendered. The CreatorPantry link is an ordinary link: no script, no tracking parameters.
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the copy and the landmarks. Wait for approval. Implement with specs. Report.
```

## PPL.19 Platform legal pages

```text
SCOPE: Render the platform legal documents on the apex: /terms, /privacy, /acceptable-use, /dmca, and the DPA and
sub-processor list if PP19.13 writes them.
- Source: markdown in docs/legal/, imported as text at build and parsed into data (headings, paragraphs, one-level
  lists, tables, links), never raw HTML.
- On arrival the page scrolls to the top and focuses its h1.
- legal-links.ts is the one list the footer reads. A document whose file is absent, or still marked as a mock-up,
  renders as plain text with "(coming soon)" in the footer, and its route is the not-found page.
CONSTRAINT: PPL-008; PP19.13 (the content); content.md's spirit (no raw HTML); IR-7, IR-10.
RESTRICTION: The parser drops reviewer notes and mock-up banners and keeps [PLACEHOLDER] brackets visible. No page
claims to be reviewed when it is not: the mock-up marker is the gate, and PPL.15's flag stays off while any linked
document carries it. Prerendered, like every public path.
UTILIZATION: design-review after implementation.
BEHAVIOR: Show the parser's data model and the gate. Wait for approval. Implement with parser specs over every real
file, and page specs. Report.
```

## PPL.20 Document head, social card, and robots

```text
SCOPE: Give every prerendered public path its head in the HTML a scraper receives:
- the title and meta description from PPL.5, and the canonical https://pantrypress.app/ (one canonical for '/' and
  '/pricing');
- Open Graph and Twitter card tags, with og:image 1200×630 and its alt text;
- the hero photo's AVIF preload, and the display font's preload.
Add npm run make:og-card, which builds public/images/og-card.png with Playwright from the real tokens, the fonts, the
logo, and the hero's headline, read out of the component. Add public/robots.txt with Disallow: /, and an X-Robots-Tag:
noindex from the gateway, until launch. Name in both places the lines that change at launch.
CONSTRAINT: PPL-009; PPL.2's prerendering; gateway.md; IR-6.
RESTRICTION: Tags set only by Angular's Meta service at runtime do not count. A spec reads the prerendered HTML files as
text and fails when a tag is missing or the preload stops naming the collage's front photo. The card photographs no
dashboard screen and no real blog. No price in any tag.
UTILIZATION: public-site-reviewer, for the apex's no-JS and head behaviour.
BEHAVIOR: Show the head for '/' and '/pricing' and the card. Wait for approval. Implement with specs. Report.
```

## PPL.21 Motion and performance, with a committed harness

```text
SCOPE: First, commit scripts/perf-landing.mjs. It:
- builds production and serves it as the gateway does: gzip or brotli, the prerendered documents, and an anonymous
  session (no ticket);
- runs Lighthouse mobile three times and reports the median LCP, CLS, TBT, and accessibility;
- counts first-load image bytes at 412 px with no scroll, in both themes;
- writes a dated JSON result under docs/performance/.
Then add, as the budget allows:
- LandingRevealDirective for every section but the hero;
- the hairline between sections (drawn once by the page, never after an empty section);
- the hover lift on cards only where a library input gives depth without a pointer cursor.
CONSTRAINT: PPL-010, PPL-011; IR-5, IR-6; rendering-style determinism (the photos are fixed); Lighthouse as a new
devDependency.
RESTRICTION: Budget: LCP under 2.5 s, CLS under 0.1, first-load images under 600 KB. Measure before and after with the
committed harness, never the dev server. No animation library. Nothing hidden by CSS. Everything still under reduced
motion, and revealed on beforeprint.
UTILIZATION: design-review after implementation; public-site-reviewer for the budget.
BEHAVIOR: Run the harness on the current page and report the baseline. Show the motion inventory. Wait for approval.
Implement. Report the before and after numbers from the committed harness.
```

## PPL.22 Composition and verification

```text
SCOPE: Compose LandingComponent in PPL.5's order (hero, value props, tour, reader view, how it works, preset gallery,
promises, pricing, FAQ, trust, final CTA; then the footer outside main). Wire Landing:Pricing. Then verify:
- anonymous, half-finished, complete, and revoked sessions at '/' and '/pricing', through the real gateway;
- every CTA and link target, including ?plan= and the closed sign-up state;
- the catalogue-down state;
- light and dark at 375, 768, and 1440 px, and 200% zoom on '/' and a legal page;
- a keyboard-only walk with every focus ring's contrast measured;
- reduced motion;
- JavaScript off on '/', '/pricing', and '/terms';
- the PPL.21 budget from the committed harness;
- axe in both themes;
- PPL.4 run again, with the capability spec green.
CONSTRAINT: PPL-001 to PPL-012; frontend.md; design-system.md; IR-7, IR-11.
RESTRICTION: Test through real routing, guards, and the gateway. Fix only findings within this document's scope. The
page is not marked done with the budget unconfirmed: report the numbers either way.
UTILIZATION: design-review, api-contract-checker, test-gap-analyzer, architecture-reviewer, and public-site-reviewer;
Playwright for the journey and screenshots.
BEHAVIOR: Run everything. Fix in-scope findings. Run npm run build, npm test, npm run lint, dotnet build, and dotnet test.
Report proof for each item, with screenshots attached.
```

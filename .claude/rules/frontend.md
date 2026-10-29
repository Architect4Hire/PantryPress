# Front end (Angular 22)

Workspace `web/`: `pantrypress-ui` (library), `pantrypress-admin` (the platform home on `pantrypress.app`),
`pantrypress-site` (SSR).

## The platform home (PP-D19, ADR 0019)

- `pantrypress-admin` builds every page on the apex. Its public routes - `/`, `/pricing`, and the legal pages - are
  prerendered at build into static HTML that reads complete without JavaScript; `/sign-in`, `/sign-up`,
  `/verify-email`, `/forgot-password`, `/reset-password`, and everything under `/dashboard` are client-rendered. Only
  the build prerenders: no Node server runs for the apex. Every link an email carries opens one of these (the
  account-security page at `/dashboard/account/security`, an invitation at `/dashboard/join`; PP2.14).
- The public paths are one list, `PUBLIC_PATHS` in `app/public-paths.ts` (PPL.2): `app.routes.server.ts` prerenders
  exactly those and renders every other route in the browser (`outputMode: "static"`, so the build emits
  `browser/<path>/index.html` per public path and `index.csr.html` for the rest, and no server). The gateway's list is
  tested against that file read as text (PPL.3), so it stays one literal array.
- A prerendered route reads no session, account, site, or price. Pricing, and anything else the server publishes, is
  fetched in the browser after the first paint and renders nothing when it cannot be read. On the server - only ever
  the build's prerender - `provideApi()` refuses every request (`refuse-on-server.ts`).
- Each page owns its one `<main>` and its one `h1`. The app shell (`app.html`) is the router outlet, the one toast
  region, and a progress bar while the router waits, drawn only after the first render in the browser so a prerendered
  page hydrates as it was rendered. Under `/dashboard` the dashboard's shell owns the `<main>` and each screen brings
  its one `h1`; after each move inside the dashboard, focus goes to that heading (PP3.9).
- No document may carry inline script: the gateway serves them under `script-src 'self'` and refuses to start on a
  build that has any (PPL.3). So the admin build turns off critical-CSS inlining (`optimization.styles.inlineCritical:
false`), and hydration runs without event replay or incremental hydration (on by default since Angular 22, and it
  brings event replay with it: `withNoIncrementalHydration()`). JSON data blocks (hydration state, JSON-LD) are not script.
  `npm run build` checks the admin build for it (`check:admin-build`). Without event replay a click before hydration
  is lost, so a control on a prerendered page works as a plain link or form first.
- The landing page lives in `features/landing/` and follows the "public page" shape in
  `src/web/PANTRYPRESS-DESIGN-SYSTEM.md` (PPL.6). Its build is `docs/prompts/pantrypress-landing-page-scrub-prompts.md`
  (PPL.*), whose honesty rules bind every claim it makes: a capability is shown as available only when it ships, and
  as "Coming soon" with its prompt named until then.
- The dashboard's routes live under `/dashboard`, joining a site from an invitation's link included
  (`/dashboard/join#token=…`; PP2.14); a new top-level path on the apex is a public page, and is added to
  `PUBLIC_PATHS`, the closed list the prerender configuration and the gateway's document serving read (a spec fails on a
  top-level route that is neither listed nor browser-only). Only the landing page's addresses, `/` and `/pricing`, are
  also in `SIGNED_IN_REDIRECT_PATHS`, the list the gateway's signed-in redirect is tested against: a signed-in owner
  can still read the legal pages (PPL.2).
- A link to a path the gateway decides for a signed-in visitor (`/`, `/pricing`) is a plain `href`, a document load,
  never a `routerLink`: drawn in the browser, the landing page would reach a signed-in visitor without the gateway's
  redirect (PPL-001).
- The session (PP3.8): `SessionClient` and `AccountClient` in `core/session/`. `signedInGuard` on `/dashboard` asks the
  gateway (`GET /session`) each time the dashboard is entered, and every refusal goes to `/sign-in` with a `next`
  under `/dashboard` and a reason - never to `/` or `/pricing`, which would loop with the gateway's 302. Every address
  under `/dashboard` matches a dashboard route (an unknown one as its not-found child), so the guard runs for all of
  them. A screen that meets `second-factor-required` offers to finish or drop the half-finished sign-in. A `next` never
  carries a fragment (`signInFor`): a fragment holds an emailed link's secret, and in a query string a reload would send
  it to the gateway (PP3.9).
- Sites in the dashboard (PP3.9): a site's screens are under `/dashboard/sites/{siteSlug}/…`, as the API's are, so a slug
  never sits beside a word of the dashboard's own (`join`, `account`). The site comes from that route alone - the site
  switcher is links to other sites' addresses, never a stored choice - and the API decides whether the caller may have
  it (`site-not-found` is one answer for no such site and no membership). The shell's navigation lists only screens that
  exist (`site-sections.ts`) and hides nothing by role.
- An invitation's link (PP3.9): `keepInvitationLink`, on the parent of the dashboard's route so it runs before
  `signedInGuard`, keeps the token from `/dashboard/join#token=…` in `PendingInvitation` (this browser's local storage)
  with an `Idempotency-Key` - kept for retries, renewed after a refusal the person can fix, because the API replays a
  stored 403 for a day - so it comes through signing in, and signing up and confirming in another
  tab, without ever entering an address. `/dashboard/join` takes it out of the address bar and accepts it as the page
  opens; the link is forgotten once accepted, refused for good, replaced, or on signing out from the account menu.
- The app's one `pp-toast-region` is in `app.html`, so every page has the polite announcer a notice speaks through;
  no screen or shell draws another.

## Everywhere

- Standalone components, signals for state, `OnPush` everywhere, typed reactive forms.
- The admin app talks only to the BFF through a typed API client generated or written from the contracts.
  No tokens in the browser.
- The admin app imports `@pantrypress/ui`; the site imports `@pantrypress/ui/themes`. Never cross them.
- Admin UI slices use `add-admin-component`; public components use `add-theme-component`.
- The "easy as pie" rule: one obvious primary action per step, autosave, preview before public changes,
  undo or revision history, plain language, no infrastructure jargon.
- WCAG 2.2 AA: keyboard reachable, visible focus, labels, no drag-only interactions (reorder by keyboard).
- No `any`. Strict TypeScript. ESLint and Prettier clean.
- Tests: component tests for logic, Playwright for flows, axe in both.

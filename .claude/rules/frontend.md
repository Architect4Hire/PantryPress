# Front end (Angular 22)

Workspace `web/`: `pantrypress-ui` (library), `pantrypress-admin` (the platform home on `pantrypress.app`),
`pantrypress-site` (SSR).

**Detail: `docs/requirements/pantrypress-frontend-spec.md`** (the platform home, session, sites, invitations, autosave).
Read its section before changing routing, the session, or an editor screen.

## The platform home (PP-D19)

- Public routes (`/`, `/pricing`, legal pages) are prerendered and read complete without JavaScript; everything else,
  `/dashboard` included, is client-rendered. No Node server runs for the apex.
- `PUBLIC_PATHS` in `app/public-paths.ts` is the one list (one literal array; the gateway tests against it). A new
  top-level path is a public page and joins it. `SIGNED_IN_REDIRECT_PATHS` holds only `/` and `/pricing`.
- A prerendered route reads no session, account, site, or price; `provideApi()` refuses every request on the server.
- No inline script in any document (`check:admin-build`): critical-CSS inlining off, no event replay or incremental
  hydration. A control on a prerendered page works as a plain link or form first.
- Links to `/` or `/pricing` are plain `href`s, never `routerLink`.
- Each page owns one `<main>` and one `h1`; focus moves to the heading after each dashboard navigation. The app's one
  `pp-toast-region` is in `app.html`.
- `signedInGuard` asks the gateway each time `/dashboard` is entered; refusals go to `/sign-in` with a `next` under
  `/dashboard`, never carrying a fragment.
- Site screens are `/dashboard/sites/{siteSlug}/…`; the site comes from the route alone, never a stored choice.
- Editors autosave (no Save button) and keep unsaved edits locally until the server has them; a `recipe-changed`
  conflict is never overwritten unasked.

## Everywhere

- Standalone components, signals, `OnPush`, typed reactive forms. No `any`; strict TypeScript; ESLint and Prettier clean.
- The admin app talks only to the BFF through a typed client. No tokens in the browser.
- Admin imports `@pantrypress/ui`; the site imports `@pantrypress/ui/themes`. Never cross them.
- Admin slices use `add-admin-component`; public components use `add-theme-component`.
- "Easy as pie": one obvious primary action per step, autosave, preview before public changes, undo or history, plain
  language, no infrastructure jargon.
- WCAG 2.2 AA: keyboard reachable, visible focus, labels, no drag-only interactions.
- Tests: component tests for logic, Playwright for flows, axe in both.

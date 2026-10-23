# 0008 — Public themes are an Angular 22 SSR application

- **Status:** Accepted, with one open item
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1, including the admin rendering choice
- **Decision ID:** PP-D08
- **Requirements:** RND-001, RND-007, ARCH-003, ARCH-005, ARCH-008, ARCH-013
- **Rules affected:** .claude/rules/rendering.md, .claude/rules/frontend.md, .claude/rules/aspire.md

## Context

The owner chose Angular 22 for the front end, with SSR for public pages (ADR 0002). Public pages need full
HTML for search engines, must work without JavaScript, and must fit a 100 KB first-party budget that includes
the Angular runtime. Tenancy has to stay on the .NET side, so the Node tier must never decide which site a
request belongs to. The admin app sits behind a sign-in, where SSR gives little benefit.

## Decision

`projects/pantrypress-site` is served by the `pantrypress-site` Node server (Angular's Node app engine). It is
added to the AppHost as a JavaScript app and exposed only through Front Door.

- **Host only:** the SSR server forwards the original Host to `PantryPress.Site`. Node never decides tenancy.
- **One fetch per page:** a route resolver fetches that route's page data (Redis, then blob, plus an
  in-process LRU). There is no other data access during SSR, and output is deterministic.
- **Islands only:** scaling and units, cook mode, ratings, comments, signup, search, and the minimal-header
  menu use `@defer (hydrate on …)`. Everything else stays server HTML.
- **Admin app:** a client-rendered Angular SPA behind the BFF (confirmed 2026-10-23).

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Angular SSR with incremental-hydration islands (chosen) | The Angular runtime counts against the budget and must be measured (PP7.2). One framework across the product. |
| .NET server-rendered pages (Razor) | No Node tier, but a second UI stack and component tree beside Angular. |
| Another JavaScript SSR framework | Possibly a smaller runtime, but a second front-end framework for one developer to maintain. |
| Full hydration | Simpler wiring, but ships runtime JavaScript for every component and breaks RND-007. |
| SSR for the admin app | SEO and first-paint gains don't matter behind a sign-in. Adds a server tier. Not chosen. |

## Consequences

- Components are SSR-safe: no `window` or `document` outside browser-only hooks, no `localStorage`, and no
  timers that affect output. The invariant-guard hook blocks web storage in the site project
  and the `@pantrypress/ui/themes` entry point.
- Golden files prove byte-identical output for the same page data and build (ARCH-005).
- CI runs the RND-007 budget check on every build of the site.
- The admin app never holds tokens; the BFF holds the session (ADR 0013).

## Open items

- **D-08:** if PP7.2 measures the Angular runtime over budget, hydration is disabled on public pages and the
  islands become small vanilla scripts. Default assumed: incremental hydration. Closes at PP7.2. See the
  [register](pantrypress.md#3-open-decide-items).

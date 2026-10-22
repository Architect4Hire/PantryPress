# Rendering and the public tier

Requirements RND-001 to RND-008, ARCH-003 to ARCH-008, ARCH-012 to ARCH-014, PP-D02, PP-D08, THM-007.

## Page data

- Page data is versioned JSON per route, built by the Worker only from published revisions and site
  settings (ARCH-004), written under a new versioned blob prefix, then one pointer switch (ARCH-006).
  Rollback is a pointer switch.
- Derived artifacts (home, archives, categories, sitemaps, feeds, search index) rebuild by job after publish
  (ARCH-014), incrementally where possible.
- Use `add-render-artifact` for any new page-data shape.

## SSR (`projects/pantrypress-site`)

- Angular SSR renders every request; Front Door caches it with a long TTL and explicit purge.
- The SSR server forwards the original Host to `PantryPress.Site`; Node never decides tenancy.
- One page-data fetch per request through the route resolver. No other data access. No SQL, ever.
- SSR-safe components: no `window` or `document` outside browser-only hooks, no `localStorage`, no timers
  that affect output.
- Output is deterministic: same page data and build produce byte-identical HTML (ARCH-005). Golden files
  prove it; update them deliberately, never in bulk.
- Pages are complete without JavaScript (ARCH-008). Interactivity uses `@defer (hydrate on …)` islands:
  scaling and units, cook mode, ratings, comments, signup, search, and the minimal-header menu.
- Design comes from the theme snapshot: tokens on the site root and `data-*` attributes. Components read
  nothing else and use container queries on the site root.

## Budgets

- Under 100 KB compressed first-party CSS and JS per page, Angular runtime included, ads excluded (RND-007).
  CI fails on a breach. At most 4 font files per page.
- LCP image gets `fetchpriority="high"`; everything below the fold is lazy.

## Structured data

- Recipe, Article, BreadcrumbList, Person, WebSite JSON-LD; `AggregateRating` from real ratings only;
  FAQPage only when the blogger opts in. Validated with Rich Results tests (RND-005).

Run `public-site-reviewer` after any change to public output.

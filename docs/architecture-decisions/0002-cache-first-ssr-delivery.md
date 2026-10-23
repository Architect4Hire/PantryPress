# 0002 — Cache-first delivery with Angular SSR on every request

- **Status:** Accepted, with one open item
- **Date:** 2026-10-22
- **Decided by:** Owner
- **Decision ID:** PP-D02
- **Requirements:** ARCH-004 to ARCH-007, ARCH-013, PNFR-001, PNFR-002, KPI-004
- **Rules affected:** .claude/rules/rendering.md, .claude/rules/public-endpoints.md, .claude/rules/aspire.md

## Context

Readers arrive from Google and Pinterest, mostly on phones, often mid-cook. Core Web Vitals are a product
target (KPI-004), and one site may spike to 50 page views a second (PNFR-004). One person operates the
platform, so incidents have to be rare by design (BIZ-008). Rendering every page from SQL would make the
database the bottleneck and a single point of failure for every reader. It would also put tenant data access
on the anonymous path.

## Decision

The `pantrypress-site` Node server renders public pages on every request, and Azure Front Door caches them, so
the edge serves most reader traffic.

Publishing builds **page data**: versioned JSON view models per route, derived from published revisions and
site settings. Page data is written to blob storage under a new versioned prefix, made live by one atomic
pointer switch, and warmed into Redis. The SSR server reads page data through `PantryPress.Site` and never
touches SQL. Dynamic features are small JSON endpoints and hydrated islands.

- **Caching:** a long shared TTL at the edge, purged explicitly on publish, settings, theme, and moderation
  changes.
- **Degraded mode:** outages of SQL, ApiService, or the Worker do not affect public pages. If the SSR server
  or site API is down, the edge keeps serving cached pages. Only islands degrade.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| SSR per request, edge cache, page data (chosen) | A page-data pipeline and correct purges to build. SQL outages are invisible to readers. |
| Static site generation to blob | Cheap and fast to serve, but every settings or theme change rebuilds every page of a site, and build time grows with site size. Islands still need an API. |
| SSR reading SQL directly | A simpler pipeline, but every cache miss hits SQL, outages reach readers, and anonymous requests reach tenant data (ARCH-013). |
| Client-rendered public SPA | Weak SEO and LCP. Fails pages-without-JavaScript (ARCH-008). |

## Consequences

- The Worker builds page data and switches the pointer. Rollback is a pointer switch (ARCH-006).
- Purge tags, cache keys, and blob prefixes include the site id (ADR 0003).
- Derived artifacts (home, archives, sitemaps, feeds, search index) are rebuilt by jobs after publish
  (ARCH-014).
- A change only reaches readers after the edge purge, so the "Publish" flow must wait for it before showing
  the live URL.

## Open items

- **D-02:** whether Front Door serves stale content on origin errors. Default assumed: long TTL plus explicit
  purge until stale-on-error is verified. Closes at PP9.4. See the
  [register](pantrypress.md#3-open-decide-items).

# Tenancy and site isolation

The site is the tenant (PP-D03). Isolation failures are the worst bug this product can have.

## Rules

1. Every site-owned entity implements `ISiteOwned` with a non-nullable `SiteId` (PP-D06 zones: site-owned,
   published public, reader-submitted).
2. A global query filter on `SiteId` applies to every `ISiteOwned` entity. It reads the current site from
   `ISiteContext`, which is set once per request.
3. `SiteId` is stamped by the `SaveChanges` interceptor from `ISiteContext`. Code never assigns it by hand,
   and an entity whose `SiteId` differs from the context fails the save.
4. **Admin requests** resolve the site from the route (`/api/v1/sites/{siteSlug}/…`) plus the caller's
   membership. No membership, no site context, 404.
5. **Public requests** resolve the site from the Host header only, through `SiteHost` (ARCH-003, PSEC-004).
   Unknown hosts get the neutral platform 404.
6. **Publishing API** requests resolve the site from the publishing key.
7. A site id is never read from a body, query string, cookie, or client header.
8. Cache keys, blob prefixes, purge tags, search indexes, and rate-limit partitions all include the site id.
   Use `SiteCacheKey` helpers; never hand-build a key.
9. `IgnoreQueryFilters()` is allowed only in `Modules/Platform/` operator code, with a comment explaining why,
   and is flagged by the invariant hook elsewhere.
10. Background jobs carry `SiteId` in their payload and set `ISiteContext` before touching data.

## Two-site test (required)

Any change that touches site-owned, public, or reader data adds a test using the two-site harness:

- two sites with similar data (same slugs, titles, recipe names);
- prove site B cannot read, list, mutate, render, cache, purge, search, or redirect to site A's data through
  the changed path;
- include host-header tricks for public paths (other site's host, `Host` vs `X-Forwarded-Host`, uppercase,
  trailing dot, custom domain of the other site).

## Review

Run `site-isolation-auditor` after any prompt that touches site-owned data.

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

## Operator tables (written exceptions)

Two shared-kernel tables carry a nullable `SiteId` without being site-owned: `OutboxMessages` (PP1.10), which the
dispatcher reads across sites, and `AuditEntries` (PP1.11), which only the operator reads across sites (OPS-006). Neither
implements `ISiteOwned` or has a query filter, so the global filter must key on `ISiteOwned`, never on a property named
`SiteId`. No route reads either table, and a boundary test keeps every file outside its own `Managers/` folder from
naming it. Until PP2.4 their writers take the site as an argument said out loud; PP2.4 takes it from `ISiteContext`
(refusing a site that differs from the context, and `null` for a `site.*` audit action) while keeping an explicit "no
site" path for account and platform work.

## Tenant root (written exception)

`Site` (PP2.2) is the tenant itself: its id is the `SiteId` every site-owned row carries. It has no `SiteId` of its own,
does not implement `ISiteOwned`, and has no query filter. It cannot have one, because resolving a request's site (rule 4
from the route and membership, rule 5 from the Host through `SiteHost`) reads it before any site context exists. Rule 1
holds for everything else, including every table that refers to a site. Because no filter stands behind it:

- Only the Sites module reaches the table; a boundary test refuses the set, the table, or its configuration anywhere
  else outside the migrations. Other modules ask a Sites contract, behind a declared module edge.
- A row is returned only for the slug or host resolution has authorized, or listed through the caller's own memberships.
  An operator-wide listing is a Sites contract that only `Modules/Platform/` may call, declared when it is added.
- A read by a slug or host the caller holds no claim to (availability, creation, the slug policy) answers existence only -
  a yes or no, or the unique index's refusal - never the row, its id, or its status (SITE-002).
- The query filter and the `SiteId` stamp key on `ISiteOwned` only, never on a property of type `SiteId`, so they never
  touch `Site.Id`. A site's id is minted on the server when the site is created (PP2.10), the one place a `SiteId` value
  is made, and never accepted from a client.
- The resolution caches are the exception to rule 8: they are keyed by the normalized slug or host, because no site id is
  known yet, and are invalidated when a slug or host changes hands (SITE-004, DOM-001), since a stale entry would send
  one site's traffic to another.
- Slugs and hosts are checked for ASCII in .NET before any lookup: the column is `varchar`, and the client's code page
  conversion could turn a look-alike letter into a valid one.

## Two-site test (required)

Any change that touches site-owned, public, or reader data adds a test using the two-site harness:

- two sites with similar data (same slugs, titles, recipe names);
- prove site B cannot read, list, mutate, render, cache, purge, search, or redirect to site A's data through
  the changed path;
- include host-header tricks for public paths (other site's host, `Host` vs `X-Forwarded-Host`, uppercase,
  trailing dot, custom domain of the other site).

## Review

Run `site-isolation-auditor` after any prompt that touches site-owned data.

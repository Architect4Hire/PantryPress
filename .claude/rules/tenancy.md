# Tenancy and site isolation

The site is the tenant (PP-D03). Isolation failures are the worst bug this product can have.

## Rules

1. Every site-owned entity implements `ISiteOwned` with a non-nullable `SiteId` (PP-D06 zones: site-owned,
   published public, reader-submitted). Both, and the `SiteId` type, are the shared kernel's (`Managers/Tenancy`,
   PP2.4).
2. A global query filter on `SiteId` applies to every `ISiteOwned` entity. It reads the current site from
   `ISiteContext`, which is set once per request: one scoped context, entered through `ISiteContextWriter`, where
   entering the same site again changes nothing and entering another throws. Only a resolver holds the writer - today
   the Sites module's facades - and a boundary test keeps every other file from naming it.
3. `SiteId` is stamped by the `SaveChanges` interceptor from `ISiteContext`. Code never assigns it by hand,
   and an entity whose `SiteId` differs from the context fails the save.
4. **Admin requests** resolve the site from the route (`/api/v1/sites/{siteSlug}/…`) plus the caller's
   membership. No membership, no site context, 404. The site policy's decision (`ISiteMembershipFacade.AuthorizeAsync`)
   is the resolution: admitting the caller enters the site, and a refused decision - 404 or 403 - enters none. The site
   policies stacked on one route are one decision, so a role that passes one and fails another enters nothing; a refusal
   by any other policy on the route ends the request before its endpoint runs (PP2.4). Only that decision enters a
   site: a question about another site the caller holds (copying to a second site) asks `FindAsync`, which enters
   nothing, because a second site in one request throws.
5. **Public requests** resolve the site from the Host header only, through `SiteHost` (ARCH-003, PSEC-004).
   Unknown hosts get the neutral platform 404.
6. **Publishing API** requests resolve the site from the publishing key.
7. A site id is never read from a body, query string, cookie, or client header. ApiService refuses to start when any
   route could bind one: a `SiteId` anywhere in a request type, or a route value, parameter, header, or property whose
   name is a site id (`SiteIdBindingConvention`, PP2.4).
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
naming it. Their writers never take a site (PP2.4): every call says `SiteScope.Current`, the site `ISiteContext`
resolved (and a bug without one), or `SiteScope.None` for account and platform work, so no record can be filed under a
site the request does not work on. A `site.*` audit action is refused with `None`.

## Tenant root (written exception)

`Site` (PP2.2) is the tenant itself: its id is the `SiteId` every site-owned row carries. `SiteMember` (PP2.3) is what
grants a site. Both are read before any site context exists - resolving an admin route (rule 4) starts from the caller's
membership, the public tier (rule 5) from the Host through `SiteHost`, and "my sites" lists across sites by account - and a
site and its first Owner are written together before there is a context to stamp from. So neither implements
`ISiteOwned` and neither has a query filter or a stamp. `Site` has no `SiteId` of its own; `SiteMember` carries a required
one, with a foreign key to `Sites`. Rule 1 holds for everything else, including every other table that refers to a site.
Because no filter stands behind them:

- Only the Sites module reaches either table; a boundary test refuses their sets, tables, or configurations anywhere else
  outside the migrations. Other modules ask a Sites contract, behind a declared module edge.
- Every membership query is keyed by the caller's account, by a site already resolved, or by both; `SiteMembers` has an
  account-leading index for that, the written exception to `SiteId`-leading indexes.
- A site row is returned only for the slug or host resolution has authorized, or listed through the caller's own
  memberships. An operator-wide listing is a Sites contract that only `Modules/Platform/` may call, declared when it is
  added.
- A read by a slug or host the caller holds no claim to (availability, creation, the slug policy) answers existence only -
  a yes or no, or the unique index's refusal - never the row, its id, or its status (SITE-002). Membership resolution
  answers no such site, a deleted site, no membership, and a suspended one alike.
- The query filter and the `SiteId` stamp key on `ISiteOwned` only, never on a property of type `SiteId` or named
  `SiteId`, so they never touch `Site.Id` or `SiteMember.SiteId`. A site's id is minted on the server when the site is
  created (PP2.10), the one place a `SiteId` value is made, and never accepted from a client.
- The resolution caches are the exception to rule 8: they are keyed by the normalized slug or host, because no site id is
  known yet, and are invalidated when a slug or host changes hands (SITE-004, DOM-001), since a stale entry would send
  one site's traffic to another. Membership itself is cached per request only (`auth.md`).
- `SiteMembers`' trigger guards losing a site's last active Owner, not having one. The facade that creates a site writes
  its first Owner in the same transaction (PP2.10); any path that takes a site out of `Deleted` must first find an active
  Owner with a locking read; account erasure (PP19.11a) marks a site its account solely owns `Deleted`, or hands it on,
  before removing that account's memberships. `TRUNCATE` and a disabled trigger skip it, which only an operator can do.
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

# 0003 — The site is the tenant

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D03
- **Requirements:** ARCH-003, ARCH-015, PSEC-004, SITE-001 to SITE-005
- **Rules affected:** .claude/rules/tenancy.md, .claude/rules/auth.md, .claude/rules/public-endpoints.md,
  .claude/rules/sites.md

## Context

An account can own several sites (Pro allows three) and can be a co-author on sites it does not own. Public
traffic arrives at thousands of hosts: subdomains, `www` variants, and custom domains. Each request has to
land on exactly one site. A cross-site leak (one blogger's drafts, subscribers, or cache entries reaching
another site) is the worst bug this product can have.

## Decision

The **site** is the tenant. An Identity account can own or belong to several sites, up to its plan limit.
Each site has members with the roles `Owner`, `Editor`, and `Author`. `PlatformAdmin` is the only
platform-wide role.

All site-owned data carries a non-nullable `SiteId`, enforced three ways:

- a global query filter;
- server-side stamping by the `SaveChanges` interceptor from `ISiteContext`;
- `SiteId`-leading indexes.

The site is resolved in one of three ways, depending on the surface:

| Surface | Site comes from |
| --- | --- |
| Admin API | The route (`siteSlug`) plus the caller's membership |
| Public site | The `Host` header only, through `SiteHost` |
| Publishing API | The publishing key (ADR 0015) |

A site id is never accepted from a request body, query string, cookie, or client header.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Site as tenant, row-level `SiteId` with global filters (chosen) | Every site-owned entity needs a filter, stamping, an index, and a two-site test. One database, low cost. |
| Account as tenant | Simpler membership, but multi-site accounts and co-authors across sites blur the boundary. |
| Database or schema per site | Strong physical isolation, but cost and migrations across thousands of sites are too much for one operator. |
| Site id supplied by the client | Easiest routing, but any client could address another site. |

## Consequences

- Every change to site-owned, public, or reader data adds a two-site test, including host-header tricks on
  public paths.
- `IgnoreQueryFilters()` is allowed only in `Modules/Platform/` operator code, with a written reason. The
  invariant-guard hook blocks it elsewhere.
- Cache keys, blob prefixes, purge tags, search indexes, and rate-limit partitions include the site id
  through `SiteCacheKey` helpers.
- Unknown hosts get a neutral platform 404. Background jobs carry `SiteId` and set `ISiteContext` first.
- `site-isolation-auditor` runs after any change to site-owned data.

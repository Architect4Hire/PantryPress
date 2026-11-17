# Tenancy and site isolation

The site is the tenant (PP-D03). Isolation failures are the worst bug this product can have.

**Detail: `docs/requirements/pantrypress-tenancy-spec.md`.** Rule numbers and section names here match it; read the
spec's text before changing anything a rule names.

## Rules

1. Every site-owned entity implements `ISiteOwned` (`Managers/Tenancy`) with a non-nullable `SiteId`. A reference
   between site-owned types is keyed `(SiteId, id)` on both sides; a type depending on a site-owned one is site-owned
   itself (join tables included); a site-owned hierarchy is one table (TPH).
2. The global query filter `site` covers every `ISiteOwned` root and reads `ISiteContext` per query. A site is entered
   once per request through `ISiteContextWriter`: the same site again is a no-op, another throws. Only the named
   resolvers hold the writer (membership decision, site creation, invitation acceptance, public host resolution, the
   Worker's outbox delivery), pinned by a boundary test. **With no site, a site-owned query throws.**
   `PantryPressDbContext` is one per scope, never pooled, registered with `AddPantryPressDbContext`.
3. `SiteId` is stamped by the `SaveChanges` interceptor, never assigned by hand; a row whose site differs from the
   context, or any site-owned write with no site, fails. `SiteId` is a concurrency token. `SiteIdUpdateGuard` refuses a
   bulk update that sets it. Raw SQL other than `FromSql` over a set is unfiltered, so it stays in repositories.
4. **Admin requests** resolve the site from `{siteSlug}` plus membership, decided only by the site policy's handler
   (`ISiteMembershipFacade.AuthorizeAsync`); a refusal enters no site. Site creation and invitation acceptance are the
   other two entries. Asking about a second site uses `FindAsync`, which enters nothing.
5. **Public requests** resolve the site from the reader's host only, through `SiteHost` (`PublicSiteResolution`): the
   `Host` header, or behind Front Door `X-PP-Reader-Host` believed only with the edge secret (or the internal token on
   the page-data call), compared in constant time - never by address, subnet, or FDID. Unknown, pending, and refused
   hosts get the neutral 404. `SiteStatusFilter` answers a closed site's status. Nothing may let a proxy rewrite Host.
6. **Publishing API** requests resolve the site from the publishing key.
6a. **An invitation's link** resolves the site from its sealed token, the server's own word.
7. A site id is never read from a body, query string, cookie, or client header; `SiteIdBindingConvention` refuses to
   start ApiService otherwise.
8. Cache keys, blob prefixes, purge tags, search indexes, and rate-limit partitions include the site id:
   `SiteCacheKey.For(ISiteContext, area, parts…)`, or `PlatformCacheKey` for what belongs to no site. Never hand-build a
   key; only declared adapters name a raw cache API (boundary test). The cache is never waited for.
9. `IgnoreQueryFilters()` only in `Modules/Platform/`, with a `// Cross-site:` reason within the three lines above.
10. Background work carries the site and enters it before touching data: an outbox message carries `SiteScope.Current`
    and its delivery enters it. The handler still checks what it reads.

## Written exceptions

- **Operator tables** (`OutboxMessages`, `AuditEntries`): nullable `SiteId`, not `ISiteOwned`, no filter; no route reads
  them and only their own `Managers/` folder names them. Writers say `SiteScope.Current` or `SiteScope.None`. The filter
  and stamp key on `ISiteOwned`, never on a property named `SiteId`.
- **Tenant root** (`Site`, `SiteMember`, `SiteHost`): not `ISiteOwned`; only the Sites module reaches them (boundary
  test). Queries are keyed by account, a resolved site, or a normalized host. A read by a slug or host the caller has no
  claim to answers existence only. Resolution caches are keyed by normalized slug or host and refreshed on every write.
  Every non-deleted site keeps an active Owner and one canonical host. Site-owned tables outside Sites have no foreign
  key to `Sites`, so erasure lists them itself. Slugs and hosts are checked for ASCII before any lookup.

## Two-site test (required)

Any change touching site-owned, public, or reader data adds a test with `PantryPress.Tests/TwoSite/` proving site B
cannot read, list, mutate, render, cache, purge, search, or redirect to site A's data through the changed path. The
floor: `AssertSiteOwnedIsolatedAsync<T>` (or `AssertAppendOnlySiteOwnedIsolatedAsync<T>`) per new entity,
`AssertReadPathIsolatedAsync<T>` per new read path, `AssertTenantRootUnchangedAsync` around tenant-root writes, and
`AssertIndistinguishableAsync` where a refusal could tell a real site from none. Public paths owe the host-header tricks
listed in the spec. The facade and route owe their own two-site tests beyond the floor.

Run `site-isolation-auditor` after any prompt that touches site-owned data.

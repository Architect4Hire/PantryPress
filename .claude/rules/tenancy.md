# Tenancy and site isolation

The site is the tenant (PP-D03). Isolation failures are the worst bug this product can have.

## Rules

1. Every site-owned entity implements `ISiteOwned` with a non-nullable `SiteId` (PP-D06 zones: site-owned,
   published public, reader-submitted). Both, and the `SiteId` type, are the shared kernel's (`Managers/Tenancy`,
   PP2.4). Since PP2.5 the model refuses what would escape the filter: a reference from one site-owned type to another
   is keyed by `(SiteId, id)` on both sides (a principal alternate key and a composite foreign key), so the database
   refuses a link to another site's row; a type that depends on a site-owned one must be site-owned itself, an implicit
   many-to-many join table included; and a site-owned hierarchy is one table (TPH), never split.
2. A global query filter on `SiteId` applies to every `ISiteOwned` entity. It reads the current site from
   `ISiteContext`, which is set once per request: one scoped context, entered through `ISiteContextWriter`, where
   entering the same site again changes nothing and entering another throws. Only a resolver holds the writer - today
   the membership decision, site creation, accepting an invitation, and the public tier's host resolution (PP2.12), four
   files in the Sites module's facades, and the Worker's outbox delivery in the kernel's outbox folder (PP2.11) - and a
   boundary test names each by its file, the kernel's own tenancy files included (PP2.14), and
   keeps every other file from naming it. The filter (PP2.5) is a model
   convention named `site`, so it reaches every `ISiteOwned` root however the type joined the model; an owned type or one
   under a base that is not site-owned is refused when the model is built. It reads the site per query, so the context may
   be made before the site is entered. **With no site, a site-owned query throws**: site work without a site is a bug,
   and an empty answer would hide it. `PantryPressDbContext` is one per scope and never pooled - a pooled context would
   share one site across requests, so it refuses pooled options - and every host registers it with
   `AddPantryPressDbContext`.
3. `SiteId` is stamped by the `SaveChanges` interceptor from `ISiteContext`. Code never assigns it by hand,
   and an entity whose `SiteId` differs from the context fails the save. Precisely (PP2.5): a new row with no site takes
   the context's, and one already naming it is let through; a changed or removed row must be the context's site before
   and after, so a site changed in place, another site's row attached and updated, and a stub removed by key all fail;
   any site-owned write with no site fails. `SiteId` is also a concurrency token, so every update and delete matches the
   row's original site in SQL as well. Bulk updates and deletes skip the save: the filter keeps them to the site's own
   rows, and `SiteIdUpdateGuard` refuses one that sets `SiteId`. Raw SQL that is not `FromSql` over an entity set
   (`SqlQuery`, `ExecuteSql`) is not filtered, which is one more reason it stays in repositories.
4. **Admin requests** resolve the site from the route (`/api/v1/sites/{siteSlug}/…`) plus the caller's
   membership. No membership, no site context, 404. The site policy's decision (`ISiteMembershipFacade.AuthorizeAsync`)
   is the resolution: admitting the caller enters the site, and a refused decision - 404 or 403 - enters none. The site
   policies stacked on one route are one decision, so a role that passes one and fails another enters nothing; a refusal
   by any other policy on the route ends the request before its endpoint runs (PP2.4). Only the site policy's handler
   asks that decision, with the slug from the route, and a boundary test keeps it so; the host refuses to start when a
   route names a site after `sites/` by anything but a required `{siteSlug}` (PP2.14). Only that decision enters a
   site, and one other: creating a site (PP2.10) enters the site it has just minted, with the caller as its Owner, so
   its audit entry is filed under it; if the save then loses the address, the request ends in that unsaved site and
   nothing after the refusal may use it. Accepting an invitation (PP2.11) is the third: the caller is no member yet, so
   it enters the site sealed in the invitation's link (rule 6a), once that site is found not deleted and before the
   invitation is read through its filter; a refusal after that ends the request in that site, with nothing filed under it. A question about another site the caller holds (copying to a second site) asks
   `FindAsync`, which enters nothing, because a second site in one request throws.
5. **Public requests** resolve the site from the host the reader asked for only, through `SiteHost` (ARCH-003,
   PSEC-004): the Host header, or, behind Front Door, the edge-proven header below.
   Unknown hosts get the neutral platform 404. Since PP2.12 `PublicSiteResolution` is that resolver: the Site host asks it
   for every request but a health probe, after the forwarding headers are stripped, with the `Host` header as it
   arrived. It splits off the port, refuses an IP literal in any spelling and anything `SiteHostNames` refuses before the
   cache or the database is asked, and enters the site only when one of its *active* hosts is exactly that host; a
   pending host, an unknown one, and a refused one are one answer, the same problem 404 an unknown path on a real site
   gets. It enters the site whatever its status - deleted, suspended, and offline included - so the status answers
   (SITE-008, SEO-007) are owed before any public endpoint reads site-owned data. Since PP2.13 they are
   `SiteStatusFilter`, a global MVC resource filter on the Site host that asks `IPublicSitesFacade.CheckStatus`, so every
   public controller answers a closed site's status before it binds or reads anything (`public-endpoints.md`). The Site host refuses to start if a
   trusted proxy may rewrite the Host (`ForwardHost`, `Edge:TrustedProxies:AllowedHosts`), and that refusal stands for
   good. Deployed, Front Door sets the origin's own Host, and from PP9.3 resolution reads the reader's host from the one
   header Front Door's rule set overwrites (`X-PP-Reader-Host`) only with the edge secret, or on the SSR server's
   page-data call with the internal service token - which the SSR server sends only after its own edge-secret check:
   by secret, compared in constant time, never by address, subnet, or FDID (owner decision, PP2.14;
   `public-endpoints.md`). Only the Sites module and the Site host name the
   resolver or its registration; only the Site host's resolution middleware sets the `IPublicSiteContext` endpoints read,
   and it must agree with `ISiteContext` (both by boundary test).
6. **Publishing API** requests resolve the site from the publishing key.
6a. **An invitation's link** resolves the site from its token (PP2.11): the site and the invitation, sealed with the data
   protection key ring ApiService and the Worker share, under a purpose of its own. Like a publishing key it is the
   server's own word, never a site a client chose, and the invitation it names is read through that site's filter, so a
   token sealed for one site never finds another's invitation.
7. A site id is never read from a body, query string, cookie, or client header. ApiService refuses to start when any
   route could bind one: a `SiteId` anywhere in a request type, or a route value, parameter, header, or property whose
   name is a site id (`SiteIdBindingConvention`, PP2.4). A sealed invitation token is not a site id a client sends
   (rule 6a).
8. Cache keys, blob prefixes, purge tags, search indexes, and rate-limit partitions all include the site id.
   Use `SiteCacheKey` helpers; never hand-build a key. A cache key (PP2.6) is `SiteCacheKey.For(ISiteContext, area,
   parts…)`: `{siteId}:{area}:{part}:…`, beginning with the site's lowercase id, taken from the context a resolver
   entered (a stand-in is refused) and never passed in, so an entry is filed only under the site the work is on. The
   area is lowercase letters, digits, and hyphens; each part is non-empty, well-formed text with `%` and `:` escaped, so
   no two inputs share a key - in .NET or in the UTF-8 Redis stores - and no part reaches another site's keys. No `{…}`
   hash-tag braces (one site would pin a Redis shard); at most 1,024 UTF-8 bytes, so a long reader value is hashed
   first, and a personal one (an email, an address) is hashed at any length. `SiteCacheKey.Prefix` covers one site's
   entries, or one area of them. What belongs to no site - the resolution caches, per-address and global rate-limit
   counters - is a `PlatformCacheKey`, `platform:{area}:{part}:…`, so even a slug spelled like a site id stays a part.
   Nothing names a raw cache API - by type, namespace, registration, package, or project alias - except a declared
   adapter, listed by its exact file, that takes only those two key types; a boundary test enforces it. The first is the
   platform cache (PP2.12): `IPlatformCache` over `RedisPlatformCache`, which takes only a `PlatformCacheKey`, on the
   connection `ServiceDefaults/Caching/CacheConnection.cs` registers; site keys join it with page data (PP7.12). Its
   entries are versioned: invalidating moves the version, and a fill writes only while the version is the one its read
   saw, so a lookup that raced a write never puts the old answer back. Once an entry has a version the hash keeps it a day
   from every write, and only the value expires with its own lifetime, as a hash field (Redis 7.4 or later); a lookup that
   took longer than `SiteHostMap.MaxFillDelay` (5 seconds) between its read and its fill caches nothing, for a version
   the cache lost. The cache is never waited for: unreachable, every call fails at once, a command gives up after 500 ms,
   and its caller carries on without it. Blob prefixes,
   purge tags, search indexes, and site rate-limit partitions build on the same rule when they arrive. ApiService's
   in-memory per-account limits (PP2.10, PP2.14) are partitioned by the account alone, which names no site and is never
   a value a client chose.
9. `IgnoreQueryFilters()` is allowed only in `Modules/Platform/` operator code, with a comment explaining why,
   and is flagged by the invariant hook elsewhere. Cross-site erasure is operator code too (ADR 0003). The comment
   starts `// Cross-site:` within the three lines above the call, and a boundary test scans every project, tests
   included, for any mention of the method outside `Modules/Platform/` or without that reason (PP2.5).
10. Background jobs carry `SiteId` in their payload and set `ISiteContext` before touching data. An outbox message
    carries it beside its payload (`SiteScope.Current`), and the delivery's scope enters it before the handler runs
    (`OutboxHandlerRegistration`, PP2.11). Only the Worker's processor makes a delivery, from the row it leased, and a
    boundary test keeps it so. The handler still checks what it reads: the site may have been deleted since.

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
grants a site, and `SiteHost` (PP2.9) is what names one from a Host. All three are read before any site context exists -
resolving an admin route (rule 4) starts from the caller's membership, the public tier (rule 5) from the Host through
`SiteHost`, and "my sites" lists across sites by account - and a site, its first Owner, and its subdomain are written
together before there is a context to stamp from. So none of them implements `ISiteOwned` or has a query filter or a stamp.
`Site` has no `SiteId` of its own; `SiteMember` and `SiteHost` carry a required one, with a foreign key to `Sites`. Rule 1
holds for everything else, including every other table that refers to a site. Because no filter stands behind them:

- Only the Sites module reaches the three tables; a boundary test refuses their sets, tables, or configurations anywhere
  else outside the migrations. Other modules ask a Sites contract, behind a declared module edge.
- Every membership query is keyed by the caller's account, by a site already resolved, or by both; `SiteMembers` has an
  account-leading index for that, the written exception to `SiteId`-leading indexes. Every host query is keyed by a
  normalized host or by a site already resolved; `SiteHosts`' unique host index is the second written exception.
- A site row is returned only for the slug or host resolution has authorized, or listed through the caller's own
  memberships. An operator-wide listing is a Sites contract that only `Modules/Platform/` may call, declared when it is
  added.
- A read by a slug or host the caller holds no claim to (availability, creation, the slug policy) answers existence only -
  a yes or no, or the unique index's refusal - never the row, its id, or its status (SITE-002). Membership resolution
  answers no such site, a deleted site, no membership, and a suspended one alike.
- The query filter and the `SiteId` stamp key on `ISiteOwned` only, never on a property of type `SiteId` or named
  `SiteId`, so they never touch `Site.Id`, `SiteMember.SiteId`, or `SiteHost.SiteId`. A site's id is minted on the server when the site is
  created (PP2.10), the one place a `SiteId` value is made, and never accepted from a client.
- The resolution caches are the exception to rule 8: they are keyed by the normalized slug or host, because no site id is
  known yet, and are invalidated when a slug or host changes hands (SITE-004, DOM-001), since a stale entry would send
  one site's traffic to another. Membership itself is cached per request only (`auth.md`). The host map (PP2.12) is
  `platform:site-host:{host}`, one entry per stored-form host, holding the site, its status, its canonical host, and its
  public name, tagline, and locale (PP2.13), or that the host names nothing. A write refreshes it after it commits,
  through `SiteHostMapDataLayer`: `RefreshAsync` with every host it added, removed, or changed, and `RefreshSiteAsync`
  for a change of the site's status, name, tagline, or locale, since every host's entry carries them. Site creation does so for its subdomain, so an address asked for before it was taken answers at
  once (tenant-provisioning G-02). A refresh that cannot reach the cache is logged and skipped, so an entry lives at most
  10 minutes (a host that names a site) or 60 seconds (one that names nothing); making the refresh survive a crash
  through the outbox is owed by the first prompt that removes a host, changes a site's status, or renames a site (PP8,
  PP10, PP14, PP17, PP19).
- `SiteMembers`' trigger guards losing a site's last active Owner, not having one. The facade that creates a site writes
  its first Owner in the same transaction (PP2.10); any path that takes a site out of `Deleted` must first find an active
  Owner and a canonical host (PP2.9) with a locking read; account erasure (PP19.11a) marks a site its account solely owns
  `Deleted`, or hands it on, before removing that account's memberships. `TRUNCATE` and a disabled trigger skip it, which only an operator can do.
- A host (PP2.9) is stored lower-case with no port or trailing dot (`SiteHostNames`, with a check beneath it), belongs to
  one site platform-wide, and never changes site or spelling, or becomes or stops being a subdomain: it changes hands by
  being removed and added again, so which site a host names changes only when a row comes or goes. Its status, kind, and
  whether it is canonical change in place, so anything cached about them - where a host redirects, whether it answers -
  is refreshed on every write to that site's hosts and every change of the site's status (PP2.12). Each site that is not deleted keeps exactly one canonical
  host, active and never a `WwwVariant`: a filtered unique index allows one, and `SiteHosts`' trigger refuses losing it,
  so moving it is one statement that sets the new and clears the old together. Like the owner trigger, it guards losing
  one, not having one; the facade that creates a site writes its subdomain in the same transaction (PP2.10).
- Slugs and hosts are checked for ASCII in .NET before any lookup: the column is `varchar`, and the client's code page
  conversion could turn a look-alike letter into a valid one.

## Two-site test (required)

Any change that touches site-owned, public, or reader data adds a test using the two-site harness:

- two sites with similar data (same slugs, titles, recipe names);
- prove site B cannot read, list, mutate, render, cache, purge, search, or redirect to site A's data through
  the changed path;
- include host-header tricks for public paths (other site's host, `Host` vs `X-Forwarded-Host`, uppercase,
  trailing dot, custom domain of the other site, and from PP9.3 `X-PP-Reader-Host` naming the other site with a
  forged, stale, empty, or missing edge secret, as a list, or repeated).

The harness (PP2.7) is `PantryPress.Tests/TwoSite/`. `TwoSiteHarness.SeedAsync` seeds two look-alike sites with fresh
ids, so tests share the migrated database:

- the same name and tagline, and slugs one letter apart, A's a prefix of B's;
- an Owner, Editor, Author, and suspended member on each;
- three accounts that cross between them: one that is Author on A and Owner on B, one suspended on A and Editor on B,
  and a stranger.

`In(site)` is a scope working on a site, `Scope()` one working on none, `ChangeAsync` writes setup rows (with a site for
site-owned ones), and `ClientFor(account)` is ApiService signed in. `TwoSiteChecks` holds what a change owes:

- `AssertSiteOwnedIsolatedAsync<T>` for every new site-owned entity. It is the floor: it proves the filter, stamp, and
  token hold for the type through `db.Set<T>()`.
  - With no site, a read throws.
  - As A, list, count, find, and ask after only A's rows.
  - B's row is untouched by a bulk update or delete.
  - These writes are refused: a new row naming B, B's row written back or removed, B's row passed off as A's (the token's
    case), and a row moved to B.
  - B's row is found exactly as written.
- `AssertReadPathIsolatedAsync<T>` for each read path the change adds beyond the set: a repository's raw query, a
  projection, or a join. Its own test shows it catching a `SqlQuery` that skips the filter.
- `AssertTenantRootUnchangedAsync` around every write to the tenant root. It proves the other site's `Sites` row and
  memberships are unchanged, row versions included.
- `AssertIndistinguishableAsync` wherever a refusal could tell a real site from none. It compares the expected status and
  code, the problem document, and the headers. Content only: timing is not compared.

The checks are a floor, not the debt: the change's own facade and route owe two-site tests through the seeded world.
PP2.10 and PP2.11 owe these for the tenant root, over ApiService:

- a list returns only the caller's sites;
- reading B by slug as A's owner answers like no site;
- a role change or removal on A leaves B's rows unchanged (the shared account's above all);
- availability never names the holder.

What a cache owes comes with the first cache: the PP7.12 adapter owes a check that a read as B misses A's entry, that
every key written as B is under B's prefix, and that clearing B leaves A. The public site joined the harness with the
host map (PP2.12): `CreateAsync(withSite: true)` starts it over the shared database and the test AppHost's Redis, and
`ReadAsAsync(host)` sends any Host through its whole pipeline to a probe route that answers what resolution decided. A
change to a host, a canonical host, a site's status, or its name, tagline, or locale owes, over it: the new answer at once after its refresh, the
other site's entry unchanged, and a lookup that raced it refused. A public path owes the host-header tricks
`SiteHostTests` sends: the other site's host, `X-Forwarded-Host` and its kin, case, a trailing dot, a port, `www.` before
either site's host, the other site's custom domain, `app.<zone>`, the bare `<zone>` itself (the platform home since
PP-D19, never a tenant), an `xn--` look-alike, IP literals, and `X-PP-Reader-Host` naming the other site (believed
from PP9.3 only with the edge secret; never before).

## Review

Run `site-isolation-auditor` after any prompt that touches site-owned data.

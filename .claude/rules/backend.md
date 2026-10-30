# Back-end rules

Applies to everything under `src/` except the AppHost. See also `tenancy.md`, `api-contract.md`,
`external.md`.

## The seam (ARCH-002)

```text
Controller → Facade → Business → DataLayer → Repository | Gateway
```

| Layer | Owns | Must not |
| --- | --- | --- |
| Controller | Routing, binding, `[Authorize]` policy, mapping the facade result to HTTP | Contain logic, touch EF Core, call a gateway |
| Facade | Use-case orchestration, transaction, site context, idempotency, outbox writes, audit | Hold business rules inline |
| Business | Rules, invariants, deterministic calculations | Do I/O |
| DataLayer | Composing repositories for a use case | Expose `IQueryable` upward |
| Repository | EF Core queries for one aggregate; the module's unit of work | Cross aggregates (one written exception below), call gateways |
| Gateway | One external provider behind a typed interface | Leak provider DTOs |

## Module-first domain (ARCH-001)

- `PantryPress.Domain/Modules/<Context>/` holds entities, value objects, business, data layer, repositories,
  and contracts for one bounded context: Accounts, Sites, Recipes, Posts, Media, Rendering, Audience,
  Monetization, Analytics, Billing, Domains, Publishing, Import, and Platform (audited operator code).
- `Managers/` is the shared kernel: clock, result types, outbox, audit, idempotency, and tenancy (`SiteId`,
  `ISiteOwned`, the site context; PP2.4). Every other id type is its module's, in its `Contracts/`, and a new row's id
  is minted by the facade that makes it (`Guid.NewGuid()`); a site's id, by site creation alone (PP2.10).
- A module's folders are the seam's layers - `Contracts/`, `Facades/`, `Business/`, `DataLayer/`, `Repositories/`,
  `Gateways/`, and `Validation/` - and its entities, their configuration, options, outbox messages, and its
  `<Module>ServiceCollectionExtensions` sit at its root. `Managers/` exists only at the domain root (PP2.1).
- A module talks to another module only through its public contracts (facade interfaces or events), and never names
  a type at another module's root. Inside a module: Contracts name nothing behind them and expose no entity or
  Identity type; Business is pure - no I/O, nothing asynchronous - and any layer may use it; nothing below a facade
  names a facade or a facade's interface; EF Core stays in repositories and entity configuration, and no query leaves
  the data layer.
- **The transaction, as built (PP2.14).** A facade owns its transaction by deciding when its one save happens, and saves
  through its data layer, never past it: a facade names no repository and no unit of work. The save itself is the
  module's unit of work (`AccountsUnitOfWork`, `SitesUnitOfWork`), which sits in `Repositories/` because it is EF Core.
  It commits what was staged, opens its own transaction inside the retry strategy where an invariant must be checked
  again under a lock (an owned-site cap, a site's open invitations), accepts changes only after the commit, and turns
  EF Core's failures into an outcome. Those outcomes, and what a save is told (`OwnedSiteLimit`), are the module's
  vocabulary in `Business/`, so every layer may name them. A rule the unit of work re-checks is the same Business rule
  the facade checked first (`SiteRoles`, `OwnedSiteLimit.Allows`, `SiteInvitationRules.HasRoomForAnother`), over the
  repository's own count where that count can take the lock; only a count whose locking read needs SQL Server's hints
  (an account's owned sites, `UPDLOCK, HOLDLOCK`) is written again, raw, in the unit of work.
  The written exception to "one aggregate": the tenant root (`tenancy.md`) is read by keyed joins inside the Sites
  repositories - a member's sites, a host's site and status - because nothing else may reach those tables.
- `ModuleBoundaryTests` enforces all of this, and refuses what would hide a reference from it: global usings, namespace
  aliases into a module, and a file whose one namespace is not its folder's. A host registers the context in its
  `Program.cs` and queries nothing itself; the migration service is the written exception.

## Conventions

- Async all the way. No `.Result` or `.Wait()`.
- Return `Result<T>` from facades for expected failures; throw only for bugs.
- Validation with FluentValidation at the facade edge; error messages are plain language.
- Time comes from `IClock`; never `DateTime.Now`.
- Ids are strongly typed (`SiteId`, `PostId`, `RecipeId`). The one place an id is a plain `Guid` is an outbox
  message's payload: that is a stored wire format, read again by a later build, so it stays primitive and the handler
  wraps the id in its type as it reads it (PP2.14).
- Money is `decimal` with currency. Recipe quantities are exact rationals (`recipes.md`).
- Nullable reference types on; warnings as errors.
- Side effects that must survive a crash (purges, emails, page-data builds) go through the transactional
  outbox (PP1.10). The facade stages a message with `IOutbox.Enqueue` *before* its save, so the message commits or
  rolls back with the change; only the Worker delivers. The site is `SiteScope.Current` (the request's) or
  `SiteScope.None`, never a value (PP2.4). A message type implements `IOutboxMessage` with a stable
  `OutboxType` name and carries ids, not data (64 KiB at most). Account email is the first real one (PP1.10b): the
  message names the account and the kind of email, and the Worker reads the address and makes any link when it sends,
  so no address or live token ever sits in the outbox. Its one handler (`AddOutboxHandler`, registered in the
  Worker) is idempotent on the message id: delivery is at least once and in no particular order. The one written
  exception is an external effect that cannot share a transaction with a record of it: account email and invitation
  email (PP2.11) are sent again on redelivery, under the same `Message-ID` so the mailbox can recognize the repeat, and
  a link email that waited longer than its link lives (PP1.12), or whose invitation is no longer open, is not sent at
  all. A handler throws `OutboxPermanentFailureException` when retrying cannot help. A site message's handler finds the
  delivery's site already entered (`tenancy.md` rule 10, PP2.11), and still checks that what it reads is there. A Worker claims only the types it has handlers for.
- Idempotency has two owners (PP1.9). Replaying the stored answer to a retried `Idempotency-Key` is host plumbing in
  ApiService, ahead of MVC, over the store in `Managers/Idempotency` (`api-contract.md`). Making the work itself safe
  to repeat - a write whose retry cannot be replayed, such as one that rotates the caller's session - stays the
  facade's, as does completing a key in the same transaction as the write, if that is ever needed.
- Security-relevant and operator actions are audited (PP1.11, ACCT-005, OPS-006). The facade records an `AuditEvent` with
  `IAuditLog.Record` immediately before its one save, as with the outbox, so the entry commits or rolls back with the
  change; a repeat that changes nothing records nothing. An entry is ids and codes only - actor, target, action,
  optional site, a short reason - never an email, name, IP address, or content, and nothing may change or remove one
  (the context's save guard and the table's trigger both refuse it). Each module owns its action codes in its
  `Contracts/` (`AccountAudit`); the site is said out loud as `SiteScope.Current` or `SiteScope.None` for account and
  platform actions, and a `site.*` action needs the request's site (PP2.4).
- Logging is structured; never log secrets, publishing keys, reader emails, or full request bodies.

## Persistence

- EF Core 10, one `PantryPressDbContext`, migrations applied only by `PantryPress.MigrationService`.
- The context is one per scope and never pooled, because its site filter and stamp read the scope's site (PP2.5).
  Every host registers it with `AddPantryPressDbContext` and adds the Aspire client's retries, tracing, and health check
  with `EnrichSqlServerDbContext`; Aspire's `AddSqlServerDbContext` pools, so no host uses it, and a test says so.
- Every migration is reviewed (Template K). No data-destroying migration without an explicit plan.
- Indexes on site-owned tables lead with `SiteId`.
- Platform reference data (PP-D06) implements `IReferenceData` (`Managers/Persistence`): a versioned list in its
  module's `Business/` is the one source, the table is seeded from it by a reviewed migration, and the app never writes
  it - the context refuses a save that adds, changes, or removes a row, and a bulk update or delete (PP4.1). Today that is
  the reserved slugs (Sites) and the recipe vocabulary (Recipes); a test lists every marked type.
- No raw SQL with string interpolation. `FromSql` with parameters only, and only in repositories.

## Tests

- Unit tests for business rules; integration tests through the facade with SQL in a container.
- Every site-owned change adds a two-site test (`tenancy.md`).

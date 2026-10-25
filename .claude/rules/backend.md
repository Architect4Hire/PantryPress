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
| Repository | EF Core queries for one aggregate | Cross aggregates, call gateways |
| Gateway | One external provider behind a typed interface | Leak provider DTOs |

## Module-first domain (ARCH-001)

- `PantryPress.Domain/Modules/<Context>/` holds entities, value objects, business, data layer, repositories,
  and contracts for one bounded context: Accounts, Sites, Recipes, Posts, Media, Rendering, Audience,
  Monetization, Analytics, Billing, Domains, Publishing, Import, and Platform (audited operator code).
- `Managers/` is the shared kernel: clock, ids, result types, outbox, audit, idempotency.
- A module's folders are the seam's layers - `Contracts/`, `Facades/`, `Business/`, `DataLayer/`, `Repositories/`,
  `Gateways/`, and `Validation/` - and its entities, their configuration, options, outbox messages, and its
  `<Module>ServiceCollectionExtensions` sit at its root. `Managers/` exists only at the domain root (PP2.1).
- A module talks to another module only through its public contracts (facade interfaces or events), and never names
  a type at another module's root. Inside a module: Contracts name nothing behind them and expose no entity or
  Identity type; Business is pure - no I/O, nothing asynchronous - and any layer may use it; nothing below a facade
  names a facade or a facade's interface; EF Core stays in repositories and entity configuration, and no query leaves
  the data layer. A facade holds its module's unit of work as the transaction it owns. `ModuleBoundaryTests` enforces
  all of this, and refuses what would hide a reference from it: global usings, namespace aliases into a module, and a
  file whose one namespace is not its folder's.

## Conventions

- Async all the way. No `.Result` or `.Wait()`.
- Return `Result<T>` from facades for expected failures; throw only for bugs.
- Validation with FluentValidation at the facade edge; error messages are plain language.
- Time comes from `IClock`; never `DateTime.Now`.
- Ids are strongly typed (`SiteId`, `PostId`, `RecipeId`).
- Money is `decimal` with currency. Recipe quantities are exact rationals (`recipes.md`).
- Nullable reference types on; warnings as errors.
- Side effects that must survive a crash (purges, emails, page-data builds) go through the transactional
  outbox (PP1.10). The facade stages a message with `IOutbox.Enqueue` *before* its save, so the message commits or
  rolls back with the change; only the Worker delivers. A message type implements `IOutboxMessage` with a stable
  `OutboxType` name and carries ids, not data (64 KiB at most). Account email is the first real one (PP1.10b): the
  message names the account and the kind of email, and the Worker reads the address and makes any link when it sends,
  so no address or live token ever sits in the outbox. Its one handler (`AddOutboxHandler`, registered in the
  Worker) is idempotent on the message id: delivery is at least once and in no particular order. The one written
  exception is an external effect that cannot share a transaction with a record of it: account email is sent again on
  redelivery, under the same `Message-ID` so the mailbox can recognize the repeat, and a link email that waited longer
  than its link lives is not sent at all (PP1.12). A handler throws
  `OutboxPermanentFailureException` when retrying cannot help, and re-resolves the site from `OutboxDelivery.SiteId`
  before calling a facade. A Worker claims only the types it has handlers for.
- Idempotency has two owners (PP1.9). Replaying the stored answer to a retried `Idempotency-Key` is host plumbing in
  ApiService, ahead of MVC, over the store in `Managers/Idempotency` (`api-contract.md`). Making the work itself safe
  to repeat - a write whose retry cannot be replayed, such as one that rotates the caller's session - stays the
  facade's, as does completing a key in the same transaction as the write, if that is ever needed.
- Security-relevant and operator actions are audited (PP1.11, ACCT-005, OPS-006). The facade records an `AuditEvent` with
  `IAuditLog.Record` immediately before its one save, as with the outbox, so the entry commits or rolls back with the
  change; a repeat that changes nothing records nothing. An entry is ids and codes only - actor, target, action,
  optional site, a short reason - never an email, name, IP address, or content, and nothing may change or remove one
  (the context's save guard and the table's trigger both refuse it). Each module owns its action codes in its
  `Contracts/` (`AccountAudit`); the site is passed out loud, `null` for account and platform actions, until PP2.4.
- Logging is structured; never log secrets, publishing keys, reader emails, or full request bodies.

## Persistence

- EF Core 10, one `PantryPressDbContext`, migrations applied only by `PantryPress.MigrationService`.
- Every migration is reviewed (Template K). No data-destroying migration without an explicit plan.
- Indexes on site-owned tables lead with `SiteId`.
- No raw SQL with string interpolation. `FromSql` with parameters only, and only in repositories.

## Tests

- Unit tests for business rules; integration tests through the facade with SQL in a container.
- Every site-owned change adds a two-site test (`tenancy.md`).

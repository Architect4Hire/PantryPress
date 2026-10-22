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
  Monetization, Analytics, Billing, Domains, Publishing, Import.
- `Managers/` is the shared kernel: clock, ids, result types, outbox, audit, idempotency.
- A module talks to another module only through its public contracts (facade interfaces or events). Boundary
  tests enforce this.

## Conventions

- Async all the way. No `.Result` or `.Wait()`.
- Return `Result<T>` from facades for expected failures; throw only for bugs.
- Validation with FluentValidation at the facade edge; error messages are plain language.
- Time comes from `IClock`; never `DateTime.Now`.
- Ids are strongly typed (`SiteId`, `PostId`, `RecipeId`).
- Money is `decimal` with currency. Recipe quantities are exact rationals (`recipes.md`).
- Nullable reference types on; warnings as errors.
- Side effects that must survive a crash (purges, emails, page-data builds) go through the transactional
  outbox.
- Logging is structured; never log secrets, publishing keys, reader emails, or full request bodies.

## Persistence

- EF Core 10, one `PantryPressDbContext`, migrations applied only by `PantryPress.MigrationService`.
- Every migration is reviewed (Template K). No data-destroying migration without an explicit plan.
- Indexes on site-owned tables lead with `SiteId`.
- No raw SQL with string interpolation. `FromSql` with parameters only, and only in repositories.

## Tests

- Unit tests for business rules; integration tests through the facade with SQL in a container.
- Every site-owned change adds a two-site test (`tenancy.md`).

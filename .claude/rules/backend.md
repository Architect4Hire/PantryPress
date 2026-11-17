# Back-end rules

Applies to everything under `src/` except the AppHost. See also `tenancy.md`, `api-contract.md`, `external.md`.

**Detail: `docs/requirements/pantrypress-backend-spec.md`.** Read its section before changing the kernel, a unit of
work, the outbox, audit, idempotency, or persistence conventions.

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
| Repository | EF Core queries for one aggregate; the module's unit of work | Cross aggregates, call gateways |
| Gateway | One external provider behind a typed interface | Leak provider DTOs |

## Modules (ARCH-001)

- `PantryPress.Domain/Modules/<Context>/` with folders `Contracts/`, `Facades/`, `Business/`, `DataLayer/`,
  `Repositories/`, `Gateways/`, `Validation/`; entities, configuration, options, outbox messages, and
  `<Module>ServiceCollectionExtensions` at the module root. `Managers/` (domain root only) is the shared kernel.
- A module reaches another only through its public contracts, never a type at another module's root. Contracts expose
  no entity or Identity type; Business is pure and synchronous; nothing below a facade names a facade; EF Core stays in
  repositories and configuration. `ModuleBoundaryTests` enforces this (no global usings or aliases into a module).
- A facade decides when its one save happens, through its data layer; it names no repository or unit of work. The
  module's unit of work commits, re-checks locked invariants with the same Business rule, and maps EF failures to
  outcomes.
- Ids are strongly typed and minted by the facade that makes the row; a site's id only by site creation.

## Conventions

- Async all the way; no `.Result`/`.Wait()`. `Result<T>` from facades for expected failures; throw only for bugs.
- FluentValidation at the facade edge, plain-language messages. Time from `IClock`, never `DateTime.Now`.
- Money is `decimal` with currency; recipe quantities are exact rationals. Nullable on; warnings as errors.
- **Outbox (PP1.10):** side effects that must survive a crash are staged with `IOutbox.Enqueue` before the save, carry
  ids not data (≤ 64 KiB), and are delivered only by the Worker through an idempotent handler. Site is
  `SiteScope.Current` or `SiteScope.None`, never a value.
- **Idempotency (PP1.9):** replay is host plumbing in ApiService; making the work safe to repeat is the facade's.
- **Audit (PP1.11):** security and operator actions record an `AuditEvent` immediately before the save: ids and codes
  only, never PII or content, and never changed or removed.
- Structured logging; never log secrets, publishing keys, reader emails, or full bodies.

## Persistence

- EF Core 10, one `PantryPressDbContext`, one per scope, never pooled, registered with `AddPantryPressDbContext`;
  migrations only by `PantryPress.MigrationService`, each reviewed; no data-destroying migration without a plan.
- Site-owned indexes lead with `SiteId` (`SiteForeignKeyIndexConvention`).
- `IReferenceData` tables are seeded from versioned lists and never written by the app. `IAppendOnly` rows are never
  changed or removed, with a declared trigger.
- No raw SQL with interpolation; `FromSql` with parameters, in repositories only.

## Tests

Unit tests for business rules; integration tests through the facade with SQL in a container; a two-site test for every
site-owned change (`tenancy.md`).

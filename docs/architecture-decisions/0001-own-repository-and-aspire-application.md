# 0001 — Own repository and Aspire application

- **Status:** Accepted
- **Date:** 2026-10-22
- **Decided by:** Owner
- **Decision ID:** PP-D01
- **Requirements:** ARCH-001; requirements section 1.2 (Product independence, Repository)
- **Rules affected:** CLAUDE.md, .claude/rules/aspire.md, .claude/rules/backend.md
- **Adopts:** CreatorPantry patterns (module-first domain, the request seam, BFF authentication, tenancy
  filters, outbox, SCRUB delivery), written fresh. No code is shared.

## Context

PantryPress is a standalone product. Nobody needs CreatorPantry to use it, and CreatorPantry is only an
optional source that publishes in. If the two shared code, they would ship in lockstep. A CreatorPantry change
or outage could then reach bloggers who never used CreatorPantry. One developer builds both, so the system has
to run locally with one command, and the same application model has to drive deployment.

## Decision

PantryPress lives in its own repository with its own .NET solution and Aspire AppHost:

```text
src/
├── PantryPress.AppHost/          # Aspire application model
├── PantryPress.ServiceDefaults/  # telemetry, health, resilience
├── PantryPress.Gateway/          # YARP BFF for the admin app (app.pantrypress.app)
├── PantryPress.ApiService/       # authenticated admin API and the publishing API
├── PantryPress.Site/             # anonymous public site API
├── PantryPress.Domain/           # Modules/<Context>/ plus Managers/ shared kernel
├── PantryPress.Worker/           # page-data builds, media, purges, feeds, imports, webhooks
├── PantryPress.MigrationService/ # applies migrations before dependents start
├── PantryPress.Tests/
└── web/                          # Angular 22: pantrypress-ui, pantrypress-admin, pantrypress-site
```

The domain is module-first. Each bounded context lives in `Modules/<Context>/`, and the shared kernel lives in
`Managers/`. Modules talk only through public contracts, and boundary tests enforce this. PantryPress takes
no package, project reference, or runtime call from CreatorPantry.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Own repository and AppHost (chosen) | Proven patterns are written a second time. No coupling; own CI, releases, and toolkit. |
| A module inside CreatorPantry | Fastest start and shared infrastructure, but PantryPress could not ship or run without CreatorPantry. Contradicts product independence. |
| Shared kernel packages used by both products | Less duplication, but both products version in lockstep, and a breaking change in either blocks the other. |
| Own repository without Aspire | Fewer moving parts, but no single local run, service discovery, or dashboard, and local and deployed topologies drift. |

## Consequences

- `aspire run` is the supported way to run the whole system locally.
- Every service calls `AddServiceDefaults()`. Resources are declared only in the AppHost.
- CreatorPantry integrates only through the publishing API (ADR 0015).
- PantryPress's patterns may drift from CreatorPantry's over time. This is accepted as the price of
  independence.

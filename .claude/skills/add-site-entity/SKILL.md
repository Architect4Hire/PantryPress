---
name: add-site-entity
description: Add a site-owned entity with SiteId, global query filter, server-side stamping, SiteId-leading indexes, a reviewed migration, and a two-site test.
---

# Add a site-owned entity

Read first: `.claude/rules/tenancy.md`, `backend.md`.

## Steps

1. Decide the data zone (site-owned, published public, reader-submitted). Reader data needs minimization and
   encryption of personal fields.
2. Create the entity in `Modules/<Context>/Domain/` implementing `ISiteOwned`; strongly typed id.
3. Configure it in its `IEntityTypeConfiguration`: required `SiteId`, indexes leading with `SiteId`,
   concurrency token if edited concurrently.
4. Confirm the global query filter and stamping interceptor pick it up (they apply to all `ISiteOwned`).
5. Add the repository.
6. Create the migration and review it (Template K): no destructive change without a plan.

## Checklist

- [ ] Two-site test: B cannot read, list, update, or delete A's rows through the repository and facade.
- [ ] Stamping test: saving with a mismatched `SiteId` fails.
- [ ] Migration applies cleanly through the migration service.
- [ ] `site-isolation-auditor` run.

---
name: add-site-entity
description: Add a site-owned entity with SiteId, global query filter, server-side stamping, SiteId-leading indexes, a reviewed migration, and a two-site test.
---

# Add a site-owned entity

Read first: `.claude/rules/tenancy.md`, `backend.md`.

## Steps

1. Decide the data zone (site-owned, published public, reader-submitted). Reader data needs minimization and
   encryption of personal fields.
2. Create the entity at the module root (`Modules/<Context>/`, `backend.md`) implementing `ISiteOwned`; strongly
   typed id.
3. Configure it in its `IEntityTypeConfiguration`: indexes leading with `SiteId`, and a `rowversion` if it is edited
   concurrently. A reference to another site-owned type is keyed by site on both sides: the principal declares
   `HasAlternateKey(e => new { e.SiteId, e.Id })`, and the dependent `HasForeignKey(e => new { e.SiteId, e.ParentId })`
   with `HasPrincipalKey(p => new { p.SiteId, p.Id })` - which also makes its index lead with `SiteId`. A
   many-to-many becomes an explicit site-owned join entity.
4. Confirm the global query filter and stamping interceptor pick it up (they apply to all `ISiteOwned`, PP2.5): a
   public `SiteId` property with a private setter, never set in code; the entity is a root (or under a site-owned
   root in one table), never an owned type. `SiteIdConverter` maps `SiteId`, and the convention makes it required and
   a concurrency token, so the configuration needs neither. The model refuses to build when a rule above is broken, and
   `SiteOwnershipModelTests` then checks the new type in the product model.
5. Add the repository.
6. Create the migration and review it (Template K): no destructive change without a plan.

## Checklist

- [ ] Two-site test: B cannot read, list, update, or delete A's rows through the repository and facade.
- [ ] Stamping test: saving with a mismatched `SiteId` fails.
- [ ] Reference test: B cannot attach a row under A's parent by id.
- [ ] Migration applies cleanly through the migration service.
- [ ] `site-isolation-auditor` run.

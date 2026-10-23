# 0007 — A new design system, `@pantrypress/ui`

- **Status:** Accepted, with one open item
- **Date:** 2026-10-22
- **Decided by:** Owner
- **Decision ID:** PP-D07
- **Requirements:** THM-003, THM-007, RND-007; requirements section 1.2 (Design system)
- **Rules affected:** .claude/rules/design-system.md, .claude/rules/frontend.md

## Context

The admin app and the public blog need different things. The admin app needs rich, interactive forms, editor
chrome, tables, and dialogs. The public blog needs SSR-safe reader components with no hydration in
primitives, inside a strict performance budget. The owner decided that nothing comes from
`@creator-pantry/ui`. Sharing token names where the meaning is shared keeps the two surfaces from drifting
apart.

## Decision

PantryPress has one Angular library with a shared token foundation and two entry points:

- `@pantrypress/ui` for the admin app: forms, editor chrome, tables, dialogs, toasts, and dashboards, in light
  and dark modes.
- `@pantrypress/ui/themes` for the public site: SSR-safe reader components and theme tokens, styled only by
  the theme snapshot (ADR 0017).

Nothing depends on `@creator-pantry/ui`.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| One library, two entry points (chosen, default) | Shared tokens and one build, but a lint rule must keep admin code out of the SSR bundle. |
| Two libraries | A hard package boundary, but a duplicated token foundation and two release flows. |
| Reuse `@creator-pantry/ui` | Less work, but couples the products (ADR 0001). The owner rejected it. |
| A third-party component library | A fast admin app, but too heavy for the public budget, and a second visual language. |

## Consequences

- The admin app imports `@pantrypress/ui`; the site imports `@pantrypress/ui/themes`. They never cross.
- Components never use literal colors; colors live only in tokens.
- A showcase app documents every admin primitive with its states.
- No CSS framework on the public side. Every public component stays inside RND-007.

## Open items

- **D-07:** one library with two entry points, or two libraries. Default assumed: one library, two entry
  points. Closes at PP3. See the [register](pantrypress.md#3-open-decide-items).

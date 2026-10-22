---
name: add-admin-component
description: Add an admin app UI slice in pantrypress-admin using @pantrypress/ui, the typed BFF client, and the easy-as-pie product rule.
---

# Add an admin component

Read first: `.claude/rules/frontend.md`, `design-system.md`. Template H.

## Steps

1. Start from the blogger's goal in plain words ("Change my brand color").
2. One obvious primary action; advanced options behind disclosure.
3. Use `@pantrypress/ui` primitives only; add a primitive to the library rather than styling one-offs.
4. Data through the typed API client; signals for state; autosave or explicit save with unsaved-changes
   guard.
5. Public-facing changes offer preview before apply and an undo or revert path.
6. Copy without infrastructure jargon.

## Checklist

- [ ] Component tests and a Playwright flow.
- [ ] axe clean; keyboard only works; no drag-only interactions.
- [ ] Loading, empty, error, and permission-denied states.
- [ ] `design-review` run.

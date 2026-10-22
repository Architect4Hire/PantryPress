---
name: add-theme-component
description: Add or change an SSR-safe public component in @pantrypress/ui/themes or the pantrypress-site app, styled only by theme snapshot tokens and data-* attributes.
---

# Add a theme component

Read first: `.claude/rules/rendering.md`, `design-system.md`, `sites.md`, and the customization spec.
Template P2.

## Steps

1. Inputs come from page data only. No services that fetch.
2. Semantic HTML first; works without JavaScript. Interactivity only as a `@defer (hydrate on …)` island.
3. Style with tokens (`var(--accent)`, `var(--r)`, …) and option attributes (`.site[data-card="index"]`).
   No literal colors. Breakpoints are container queries on `.site`.
4. Cover every relevant option value and both modes.
5. Keep the component inside the RND-007 budget.

## Checklist

- [ ] Golden files per relevant option value, updated deliberately.
- [ ] axe in light and dark modes for each preset.
- [ ] Overflow check at 360, 600, 900, 1280px container widths.
- [ ] Budget check passes.
- [ ] `public-site-reviewer` run.

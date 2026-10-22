# Design system: @pantrypress/ui

PP-D07, PP-D17, THM-001 to THM-013. One library, two entry points (DECIDE default).

## Shared token foundation

- Colors live only in tokens. Components never use literal colors.
- Admin and public tokens share names where meaning is shared; public tokens follow the theme snapshot
  contract in the customization spec section 4: `--L-*` and `--D-*` color pairs, shared `--f-display`,
  `--f-body`, `--display-weight`, `--display-track`, `--base`, `--hscale`, `--r`, `--img-r`, `--measure`.

## `@pantrypress/ui` (admin)

- Forms, editor chrome, tables, dialogs, toasts, dashboards. Light and dark modes.
- A showcase app documents every primitive with states.

## `@pantrypress/ui/themes` (public)

- SSR-safe reader components only. No hydration inside primitives.
- Options are expressed as `data-*` attributes on the site root (`data-header`, `data-hero`, `data-card`,
  `data-steps`, `data-btn`, `data-texture`, `data-mark`, `data-layout`, `data-mode`) and handled in CSS.
- Layout breakpoints are container queries on `.site`.
- Fonts are self-hosted from the curated registry. No Google Fonts or remote fonts at runtime.
- No CSS framework. Every component stays inside the RND-007 budget.
- Every option value has golden and axe tests in both modes.

## Accessibility

- WCAG 2.2 AA in every reachable combination. Text 4.5:1, UI components 3:1, visible focus, reduced motion
  respected, landmarks and skip link in every layout.

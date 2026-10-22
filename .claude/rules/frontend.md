# Front end (Angular 22)

Workspace `web/`: `pantrypress-ui` (library), `pantrypress-admin` (SPA), `pantrypress-site` (SSR).

- Standalone components, signals for state, `OnPush` everywhere, typed reactive forms.
- The admin app talks only to the BFF through a typed API client generated or written from the contracts.
  No tokens in the browser.
- The admin app imports `@pantrypress/ui`; the site imports `@pantrypress/ui/themes`. Never cross them.
- Admin UI slices use `add-admin-component`; public components use `add-theme-component`.
- The "easy as pie" rule: one obvious primary action per step, autosave, preview before public changes,
  undo or revision history, plain language, no infrastructure jargon.
- WCAG 2.2 AA: keyboard reachable, visible focus, labels, no drag-only interactions (reorder by keyboard).
- No `any`. Strict TypeScript. ESLint and Prettier clean.
- Tests: component tests for logic, Playwright for flows, axe in both.

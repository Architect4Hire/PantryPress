# PantryPress design system

`@pantrypress/ui` is PantryPress's own design system (PP-D07, ADR 0007): one Angular library in
`projects/pantrypress-ui`, one shared token foundation, two entry points. Nothing in it comes from
`@creator-pantry/ui`, as code or as a copy. The rules it answers to are `.claude/rules/design-system.md` and
`frontend.md`; the public token contract is section 4 of `docs/requirements/pantrypress-customization-spec.md`.

D-07 (one library or two) is open until PP3 closes it. This document assumes the default: one library, two entry points.

## Principles

1. **Tokens are the only place a color lives.** A component names a role (`var(--accent)`), never a hex, `rgb()`,
   named color, or hue name. Literal values appear in token files and nowhere else.
2. **Typed options, not styling.** A component offers a closed set of variants and sizes. Nothing takes a class, a
   style, or a color from its caller. On the public side the options are the theme snapshot's `data-*` attributes and
   nothing else (PP-D17).
3. **One tree serves every look.** A preset, a mode, or a plan is a change of token values and attributes, never a
   fork of a component (THM-001, PP-D17).
4. **Accessible in every reachable combination.** WCAG 2.2 AA is measured, not eyeballed, in light and dark
   (THM-003). See "Accessibility".
5. **Server-safe by default.** Public components render on the server and read the same with JavaScript off
   (ARCH-008); interactivity is a hydration island in the site, never inside a primitive.
6. **Small on purpose.** No CSS framework and no third-party component kit. Public pages stay within RND-007's
   100 KB and four font files.
7. **Easy as pie.** One obvious primary action per step, plain words, no infrastructure jargon in any label, hint,
   or error (`frontend.md`).

## The two entry points

|                   | `@pantrypress/ui`                                                                                                                                  | `@pantrypress/ui/themes`                                                                                    |
| ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Source            | `projects/pantrypress-ui/src/`                                                                                                                     | `projects/pantrypress-ui/themes/src/`                                                                       |
| For               | The platform home on `pantrypress.app`: the dashboard, sign-in and sign-up, and the platform's own public pages (the landing page, pricing, legal) | Tenant blogs: `{slug}.pantrypress.app` and custom domains                                                   |
| Imported by       | `pantrypress-admin` only                                                                                                                           | `pantrypress-site` only                                                                                     |
| Holds             | Forms, editor chrome, tables, dialogs, toasts, dashboards; PantryPress's own brand tokens and fonts                                                | SSR-safe reader components and the theme token contract                                                     |
| Color values from | Static token files in the library (PP3.2; the public-page additions in PPL.6)                                                                      | The site's theme snapshot, written onto `.site` by SSR; the Market preset's values are the defaults (PP7.4) |
| Rendered          | In the browser, except the prerendered public routes, which render once at build                                                                   | On the server for every request (PP-D08)                                                                    |
| First filled by   | PP3.2 to PP3.6                                                                                                                                     | PP7.4, PP7.4a                                                                                               |

- The two never import each other, by package name or by relative path. ESLint refuses the wrong entry point in each
  app and any admin import from `themes/`, and ng-packagr fails a relative import out of `themes/`. Nothing yet lints
  `src/` importing `themes/`; until a rule does, review catches it.
- The platform's public pages are PantryPress's brand, not a tenant theme, so they use `@pantrypress/ui`, never
  `/themes` (PP-D19). Because they are prerendered on Node, any `@pantrypress/ui` component they use must render
  without browser globals at build time and read complete without JavaScript.
- `/themes` code also passes `eslint.ssr-safety.js`: no `window`, `document`, web storage, timers, `Math.random`,
  `Date.now`, or `new Date()`. A browser-only hook opts out per line with a reason.
- Each entry point exports only through its `public-api.ts`. Anything not exported there is private to the library.

## Token architecture

### What is shared

The foundation is shared as a vocabulary, not as code. The entry points cannot import each other, and public values
are computed on the server for each site, so what the two sides share is **names and meanings**: where a token means
the same thing on both sides it has the same name, and where the meaning differs, so does the name.

### Layers

1. **Pairs.** Every color token has a light and a dark value, `--L-<name>` and `--D-<name>`.
2. **Active tokens.** `--<name>` is chosen from its pair by `data-mode` (`light`, `dark`, or `auto`) on the root: `.site`
   on a blog (spec 4.3), the app root on the platform home. `auto` follows `prefers-color-scheme` in CSS alone, so a
   prerendered or SSR page gets its mode with JavaScript off. `color-scheme` is set with it. The platform home sets
   `data-mode` on `<html>`, so the page background and scrollbars follow it; with none it is `auto`, and an unknown
   value is light. Any element may set its own, so one page can show both modes (the showcase). `data-mode` is
   reserved: no component input is reflected under that name.
3. **Components** read active tokens only. A component never reads an `--L-*` or `--D-*` token, never picks a mode
   itself, and never mixes or lightens a color: derived colors are tokens too, computed in a token file (admin) or on
   the server (public, CUS-STD-04).

Tokens that do not change with mode (fonts, sizes, radius, motion) have one value and no pair. A token built from a
color token (a shadow) is declared again wherever a mode is set, so it takes that mode's color.

The platform home's tokens are `projects/pantrypress-ui/src/styles/tokens.css` (PP3.2), exported as
`@pantrypress/ui/tokens.css` and loaded by `pantrypress-admin` ahead of its own styles. `tokens.spec.ts` beside it
measures every pair that can meet in both modes, requires every role to be measured or named as exempt, and refuses a
literal color outside an `--L-*` or `--D-*` value, there and in every other style source of the library and the admin
app. A new role is a pair, an entry in the light, dark, and auto blocks, and its contrast pairs, in one change.

The admin chrome departs on purpose from the Studio mockup (`docs/design/mockups/pantrypress-studio-mockup.html`): it
uses the spec's role names, not the mockup's `--c-*` names, and PantryPress's teal and orange, not the mockup's indigo.

### Color roles

Shared, with the meanings in spec 4.1, except where the table says otherwise:

| Role          | Meaning                                                                                         |
| ------------- | ----------------------------------------------------------------------------------------------- |
| `bg`          | Page background                                                                                 |
| `surface`     | Raised or tinted panels                                                                         |
| `ink`         | Body text                                                                                       |
| `muted`       | Secondary text; still 4.5:1 on `bg` and `surface`                                               |
| `line`        | Borders and input outlines; 3:1 on `bg` and `surface` (see below)                               |
| `accent`      | Brand fill: primary buttons, active states                                                      |
| `accent-ink`  | Text and icons on `accent`                                                                      |
| `accent-text` | The brand color as text on `bg`: links (underlined in running text)                             |
| `hl`          | Highlight fill: badges, stars; on the platform home always under `hl-ink`, never a mark on `bg` |
| `hl-ink`      | Text on `hl`                                                                                    |

On a blog the brand is the blogger's; on the platform home it is PantryPress's. The meaning is the same, so the name is.

`line` is the one drift. Spec 4.1 calls it "borders and dividers", and its public tone values are decorative (about
1.3:1). The platform home's `line` outlines inputs, so it is held to 3:1, and its decorative dividers are `line-subtle`.
A public input border drawn in the spec's `line` would fail WCAG 1.4.11; PP7.4 and PP11 settle the public side.

Fills hold text, never a focusable control: `accent`, `hl`, and the status fills carry a label in their `-ink`, and
anything focusable sits on `bg` or `surface`, so the focus ring always has a surface to stand on. A notice or toast is
a `surface` panel with a status border or icon, not a status fill. An icon, border, or dot drawn in a status color on
`bg` or `surface` uses its `-text` role.

Admin only, because a blog has no use for them:

| Role                                   | Meaning                                                                                              |
| -------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `danger`, `warning`, `success`, `info` | Status fills, each with `-ink` (text on the fill) and `-text` (the color as text on `bg`)            |
| `line-subtle`                          | Decorative dividers only, never the one thing that shows where a control is, so no contrast floor    |
| `focus`                                | The focus ring; at least 3:1 against every surface it can sit on                                     |
| `overlay`                              | The scrim behind a dialog or drawer                                                                  |
| `accent-hover`, `accent-active`        | Interaction states of the brand fill; other roles add states the same way when a primitive needs one |

A status is never told by color alone: it carries a word or an icon as well.

### Other foundations

Shared names (spec 4.2): `--f-display`, `--f-body`, `--display-weight`, `--display-track`, `--base` (the body size),
`--r` (corner radius), and `--measure` (the reading width). `--hscale` and `--img-r` are public only.

Admin families, whose steps PP3.2 sets:

| Family    | Names                               | Notes                                                                                                                                                          |
| --------- | ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Type      | `--text-<step>`, `--leading-<step>` | Fixed `rem` steps, so zoom and the browser's text size work; `--text-2` is `--base`, the body size, and `--text-1` is for hints and meta only, never body text |
| Space     | `--space-<n>`                       | One scale for padding, gaps, and margins                                                                                                                       |
| Radius    | `--r`, `--r-sm`, `--r-full`         |                                                                                                                                                                |
| Elevation | `--shadow-<n>`                      | Shadow colors are tokens with pairs                                                                                                                            |
| Motion    | `--dur-<step>`, `--ease-<name>`     | Every duration is `0.01ms` under `prefers-reduced-motion: reduce`: instant, but `transitionend` still fires                                                    |

Fonts are self-hosted WOFF2 with explicit loaded weights and `font-display: swap`; no remote font and no font-loading
script anywhere. A blog's fonts come from the curated registry (THM-006); the platform home's are PantryPress's own.

## Accessibility

WCAG 2.2 AA everywhere, in light and dark, is the floor (THM-003, CMP-006).

- **Contrast.** Text 4.5:1; large text, icons, input borders, and other UI parts 3:1. Measured by a spec over every
  pair of tokens that can meet, in both modes, never judged by eye. On a blog the server corrects colors and refuses
  any it cannot fix (THM-005).
- **Focus.** Every interactive element shows a visible ring in `--focus`, drawn as an `outline` (so it survives forced
  colors) outside the element with an offset, so it sits on the surface behind, never on the control's own fill; at
  least 3:1 against every surface it can sit on, never removed without a replacement, and never hidden behind a sticky
  header or toast (2.4.11).
- **Links.** A link in running text is underlined: `accent-text` is not far enough from `ink` to tell a link by color
  alone (1.4.1).
- **Keyboard.** Everything works from the keyboard in a sensible order. Nothing needs dragging: a reorder has buttons or
  keys (2.5.7). Dialogs and drawers hold focus while open and give it back when they close.
- **Targets.** At least 24 × 24 CSS px, or spaced so a 24 px circle around each touches no other (2.5.8).
- **Forms.** Every field has a visible label; hints and errors are tied to it with `aria-describedby`; an error says in
  plain words what went wrong and how to fix it, and is not shown by color alone.
- **Structure.** Every layout has landmarks and a skip link; headings go in order without gaps.
- **Announcements.** Toasts and async results speak through a polite live region; an error that blocks the task is
  assertive.
- **Reflow and zoom.** Pages work at 320 CSS px wide and at 200% zoom without sideways scrolling.
- **Motion.** `prefers-reduced-motion` is respected; nothing flashes.
- **Tests.** Each primitive has axe tests in both modes and in every state the showcase shows (PP3.6). Each public
  option value has golden and axe tests in both modes (PP7, PP11), and each preset a manual keyboard and screen-reader
  check (THM-003).

## Naming

| What              | Rule                                                                                                      | Example                                        |
| ----------------- | --------------------------------------------------------------------------------------------------------- | ---------------------------------------------- |
| Library selectors | `pp-` elements in kebab-case, `pp` attribute directives in camelCase (ESLint)                             | `pp-button`, `ppTooltip`                       |
| App selectors     | `ppa-` in `pantrypress-admin`, `pps-` in `pantrypress-site` (ESLint)                                      | `ppa-site-switcher`                            |
| Files             | kebab-case, one component per file, its spec beside it                                                    | `text-field.ts`, `text-field.spec.ts`          |
| Color tokens      | The role, never the hue, with the `--L-` and `--D-` pairs behind it                                       | `--accent`, `--L-accent`                       |
| Scale tokens      | Family, then an ascending step                                                                            | `--space-3`, `--shadow-1`                      |
| Variants          | A typed input, reflected on the host as a `data-*` attribute that the styles select on; never `data-mode` | `variant="primary"` → `data-variant="primary"` |
| Public options    | The spec's attribute names on `.site`, unchanged                                                          | `data-header="split"`                          |

Tokens carry no `pp-` prefix: the public names are fixed by the spec, the shared names have to match them, and no
third-party CSS shares the pages.

## How the rules are kept

| Rule                                       | Kept by                                                                                                   |
| ------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| The entry points never cross               | ESLint in each app and in `themes/`; ng-packagr                                                           |
| `/themes` is server-safe and deterministic | `eslint.ssr-safety.js`; golden files (ARCH-005)                                                           |
| No literal color                           | Specs added with the tokens (PP3.2, PP7.4); `scripts/check-public-page.mjs` for the public page (PPL.6)   |
| Contrast and focus ring                    | A contrast spec over the token pairs in both modes (PP3.2, PPL.6); the server's contrast report (THM-005) |
| Accessibility                              | axe in component and Playwright tests, both modes                                                         |
| Budget                                     | `npm run budget` (RND-007)                                                                                |

## The public page

PPL.6 fills this section (PP-D19): what a platform public page may draw itself, and what it must take from the library.

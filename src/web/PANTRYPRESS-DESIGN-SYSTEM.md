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
uses the spec's role names, not the mockup's `--c-*` names, and PantryPress's teal and orange, not the mockup's indigo;
its switch is a track and thumb, not the mockup's plain checkbox; and its controls are 40 px tall in `--base` text with
`--r-sm` corners, not the mockup's 13 px inputs.

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

| Role                                   | Meaning                                                                                                         |
| -------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `danger`, `warning`, `success`, `info` | Status fills, each with `-ink` (text on the fill) and `-text` (the color as text on `bg`)                       |
| `line-subtle`                          | Decorative dividers only, never the one thing that shows where a control is, so no contrast floor               |
| `disabled`, `disabled-ink`             | A disabled control's fill and its words; exempt from contrast (WCAG 1.4.3, 1.4.11), never dimmed with `opacity` |
| `focus`                                | The focus ring; at least 3:1 against every surface it can sit on                                                |
| `overlay`                              | The scrim behind a dialog or drawer                                                                             |
| `accent-hover`, `accent-active`        | Interaction states of the brand fill; other roles add states the same way when a primitive needs one            |

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

## Primitives

The admin primitives (PP3.3 on) sit on the browser's own elements wherever one exists, so native behavior comes free:
form submission, `routerLink`, `autocomplete`, the keyboard, and Angular's reactive forms.

| Primitive                                                            | Use                                               | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| -------------------------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `button[pp-button]`, `a[pp-button]`                                  | An action with words                              | `variant` (`primary`, `secondary`, `quiet`, `danger`; secondary by default, so a primary is chosen), `size` (`md`, `sm`), `busy`. A `<button>` is `type="button"` unless it says otherwise.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `button[pp-icon-button]`, `a[pp-icon-button]`                        | An action shown only as an icon                   | The `label` is required and becomes its name. The caller's projected `<svg>` must be `aria-hidden` and carry its own 20 × 20 size; nothing checks either.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `pp-field`                                                           | A label, hint, and error around one text control  | The hint sits above the control and the error below it. The control takes the field's id and description through `PP_FIELD`, and `aria-invalid` while an error shows.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `input[pp-text-field]`, `textarea[pp-textarea]`, `select[pp-select]` | Text and list choices                             | Native attributes pass through. A caller's own `aria-describedby` and `aria-invalid` are joined with the field's, never replaced. `id` is an input: outside a field the control keeps it, inside one the field's id wins. Outside a field the control needs its own name.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `pp-checkbox`, `pp-switch`                                           | Yes or no; on or off at once                      | A label wraps the native checkbox, so the words are part of the target. A switch is `role="switch"` and differs by the thumb's place. Reactive forms or `[(checked)]`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `pp-radio-group`                                                     | One of a few, all shown                           | A `fieldset` and `legend` over native radios sharing a name, so the arrow keys move between them. Typed `options`, compared with `Object.is`. Touched once focus leaves the whole group.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `pp-card`                                                            | A raised panel grouping related content           | A `surface` with a `line-subtle` border. No heading of its own, so the caller's keeps the page's levels in order.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `pp-page-header`                                                     | The top of a dashboard page                       | The page's one `h1`, an optional sentence, and actions in `ppPageActions`, at most one of them primary.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `pp-tabs`, `pp-tab`                                                  | Panels of one page, one at a time                 | The ARIA tabs pattern: one tab stop; the arrow keys (turned around right to left), Home, and End move, and moving selects; with Alt, Control, or Meta they are the browser's. `[(selected)]`; the first tab when none, or an unknown key, is chosen. Each `pp-tab` is a direct child. Dashboard only: a hidden panel needs the app running, so never on a prerendered page. The ring is drawn inside a tab, the one exception to the offset ring, because the scrolling list would clip it.                                                                                                                                                                                                                                                                                                                                          |
| `pp-toolbar`                                                         | A row of related controls                         | `role="toolbar"`, named by its `label`. The arrow keys, Home, and End move between its buttons and links, with one tab stop checked after every render, so a control removed, enabled, or added never leaves none or two. A text field or select in it keeps its own arrow keys and tab stop. Tab stops are set in the browser only, so a server render keeps every control reachable.                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `pp-dialog`, `pp-drawer`                                             | A modal question or task; a side panel            | The native `<dialog>` with `showModal()`, named by its heading, closed by Escape, its Close button, `close()`, or `[(open)]`, and by a backdrop click only when `dismissible`, only when the press began on the backdrop, and never for an alert. Focus goes to the control marked `ppInitialFocus` (or a focusable one inside it; `autofocus` works too), else the body's first usable control, else the heading - never the footer by itself - and back to the opener on closing, or when the dialog is removed while open. `kind="alert"` is for a question that must be answered first: its body describes it, and it marks its least destructive action `ppInitialFocus`. Actions go in `ppDialogActions`; the body scrolls, the heading and actions stay.                                                                      |
| `pp-dropdown`, `a[pp-dropdown-item]`, `button[pp-dropdown-item]`     | A short list of links and buttons behind a button | The disclosure pattern, not an ARIA menu: the button says `aria-expanded` and `aria-controls`, the items are ordinary links and buttons in the tab order, and the panel is `hidden` while closed, so a server render draws it closed. `prefix` and `label` are both shown and form the button's name, joined by a real space (2.5.3). `caption` is a plain line at the top. Escape closes it and puts focus back on the button, going no further while open; so does choosing an item. A press outside closes it, and so does Tab leaving it; a press that focuses nothing (Safari's) never closes it before the item's click lands. An item marked `aria-current` shows it in weight and a bar, never color alone. `align` (`start`, `end`); `[(open)]`. For choices that move the reader, never a `select` that navigates (3.2.2). |
| `pp-toast-region`, `PpToaster`                                       | What just happened                                | One region in the shell, a "Notifications" landmark. `show()` adds a toast and says its tone and words once; the list itself is not live, so its buttons are not read out. A success or note with no action goes after 8 seconds, held while hovered or focused; anything else waits to be closed (WCAG 2.2.1). At most three that leave by themselves show, the oldest of them going first; one that waits is never sent away. When a focused toast goes, focus moves to the next one, else back where it came from. `announce()` speaks words without drawing them, and only the latest is kept.                                                                                                                                                                                                                                   |
| `pp-notice`                                                          | A status in the page's flow                       | Its tone in words and an icon. `announce`, for one that appears after load, makes a warning or error an alert, and says a note or success politely through the announcer once it appears. `dismissible` says `dismissed`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `pp-empty-state`                                                     | A page with nothing in it yet                     | A heading at the `level` that fits (2 to 4), a sentence, and the one obvious next step.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `pp-progress`                                                        | How far along something is                        | The native `<progress>`, named by its label; with no `value` it says only that work goes on, and stops moving under reduced motion. `showValue` adds the percentage, hidden from screen readers, which hear the bar's own value.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `pp-status-pill`                                                     | A short status beside an item                     | Its words carry the meaning. A toned pill is its status fill with that status's ink; a neutral one is a bordered surface.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `pp-list`                                                            | Everything around a list the app draws            | A region named by its `label`. Slots for `ppListSearch`, `ppListFilters`, and `ppListEmpty`; a "Sort by" select over typed `sortOptions` with `[(sort)]`; Previous, Next, and "Page n", since lists page by cursor and know no total (the app keeps the cursors). `status` is `loading`, `ready`, or `error`: the first load shows progress, a later one keeps the rows in view marked `aria-busy`, an error is an alert with Try again (`retry`), and `empty` shows the empty slot. Paging moves focus to the rows' top (the list itself when none show), and only the button pressed shows busy; Try again, and rows hidden while holding focus, leave it on the list. The first load says "Loading posts" from the label. Each answer after the first says `resultsText` once.                                                    |
| `pp-search-field`                                                    | Words to look for in a list                       | A search landmark around a labelled `<input type="search">` and a Search button. `[(query)]` follows each keystroke; `searched` says the trimmed words on Enter or Search, and `''` once the field is emptied (Clear, Escape, or deleting), so the whole list returns. Escape with words clears them and goes no further; a second reaches a dialog. No debounce: the app waits, if it searches as the reader types. Reactive forms.                                                                                                                                                                                                                                                                                                                                                                                                 |
| `pp-chip-group`                                                      | Filters, shown as chips                           | A `fieldset` and `legend` over native checkboxes, each wrapped by its chip, so Tab and Space work; the value is an array in the options' order. `multiple="false"` makes them radios (one value or null, the arrow keys move). A chosen chip has the accent fill and a check mark, never color alone; the mark's room is kept when hidden, so no chip moves. A form's type must match the mode. Touched once focus leaves the group.                                                                                                                                                                                                                                                                                                                                                                                                 |
| `pp-uploader`                                                        | Choosing files and following them up              | A visible button (`variant`, secondary or primary) opens the browser's own picker; a drop on the area is the extra, never the only way (2.5.7). `accept` and `maxBytes` are a courtesy check, naming each refused file and why in a dismissible warning; the server still sniffs and scans every file (MED-001). `filesChosen` gives what passed; the app sends. Its `items` draw as progress, then Added or Not added with the error, Try again (`retry`, leaving focus on that file's Remove), and Remove (`remove`, stopping a send; focus goes to the next file's Remove, else the previous, else the button, else the uploader). A drop is held even while disabled, so the browser never opens the file. It says once how many files were chosen or refused, and each that is added or fails, never progress.                  |

- **Busy** keeps a button focusable and marks it busy and unavailable (`aria-busy`, `aria-disabled`); the spinner has
  no words, so a screen reader hears only "unavailable", and a task that needs its progress said aloud says it in its
  own live region (PP3.4). A press is dropped in the capture phase, before the element's own handlers, `routerLink`
  included, and every ancestor's bubbling handler. It does not stop an ancestor's capturing handler, a middle-click on
  a busy link, or a form's `requestSubmit()`.
- **Status panels.** Toasts and notices are `surface` panels with a border and icon in the tone's `-text` color and the
  tone in hidden words ("Success:", "Error:"); never a status fill, so their buttons have a surface to sit on.
- **The one timer** is a toast's, started after render in the browser and opted out of `eslint.ssr-safety.js` on its
  line. Nothing else in the library waits on time.
- **Data primitives hold no data** (PP3.5). The list, search field, chips, and uploader take plain inputs and say what
  the reader did; fetching, cursors, debouncing, and sending files are the app's, through its typed API client. A
  server render writes no DOM property, so state a first paint must show (a chosen sort) is bound as an attribute too.
- **Speaking.** Everything the library says aloud goes through one polite announcer: the shell's, in the toast region,
  or, while a dialog or drawer is open, the one inside it, because a modal makes the rest of the page inert and so
  unheard. Only one toast region belongs in an app; a second would draw everything twice.
- **Dialogs in tests.** jsdom has no `showModal()`, so the specs use `testing/dialog.ts`, a stand-in that opens and
  closes the element and fires `close` at once, where a browser queues it (a queued close after a reopen is ignored,
  and a spec proves it). The inert page, the trapped focus, Escape's own `cancel`, the backdrop (which reads
  `--overlay` where the browser lets `::backdrop` inherit), and what a screen reader actually says are proven in the
  showcase's browser run (PP3.6).
- **Errors** are the caller's words, shown with an icon and a hidden "Error:" so they are never color alone. A field
  marks itself optional in words; a required one sets the native `required` on its control.
- **Checked state** is written to the element whenever it changes, as well as bound, so a value set and undone before
  the view refreshes still shows truly.
- **Ids** come from `PpIdSequence`, a counter per application rather than per process, never a random value: every
  page starts at 1 wherever it renders, so a prerendered page is the same on every build and its ids match the
  browser's.
- **Server-safe.** The platform's public pages prerender with these, so `primitives/` passes `eslint.ssr-safety.js`,
  and `server.spec.ts` renders every primitive with `renderApplication`, twice, to the same HTML ids, with dialogs
  closed, no toast, and the toolbar untouched. That spec is alone in its file because a server render puts its DOM
  classes on the global object.
- **Tests.** The specs run axe (`testing/axe.ts`, WCAG 2.x A and AA, 2.2 included) over every primitive in every state
  they cover: the text controls in `field.spec.ts` and `control.spec.ts`, the switch in `checkbox.spec.ts` and
  `choice.spec.ts`. jsdom draws nothing, so color contrast is `tokens.spec.ts`'s, and both modes as drawn are the
  showcase's (PP3.6).
- **The showcase** (PP3.6) is `/showcase` in the admin app's development build only: `dev-routes.ts` is empty, and the
  development configuration swaps in `dev-routes.development.ts`, so a production build carries none of it and the
  apex's closed list of public paths never meets it. Every primitive has a section whose states are drawn twice, in a
  `data-mode="light"` panel and a `data-mode="dark"` one, each with its own live state; the page's own mode, which the
  toasts follow, is a choice that sets `data-mode` on `<html>`. A new primitive owes a section there: `showcase.spec.ts`
  fails when a component the library exports is missing from either panel. `npm run e2e` (Playwright, in Chromium,
  Firefox, and WebKit) runs axe over it with color contrast on, in every page mode and with every dialog, the drawer,
  toasts, the list's other states, and the uploader's refusals showing. It also checks how the modes resolve, nested
  and under each system preference, the native modal dialog, the radios' arrow keys, and the focus ring.

## Accessibility

WCAG 2.2 AA everywhere, in light and dark, is the floor (THM-003, CMP-006).

- **Contrast.** Text 4.5:1; large text, icons, input borders, and other UI parts 3:1. Measured by a spec over every
  pair of tokens that can meet, in both modes, never judged by eye. On a blog the server corrects colors and refuses
  any it cannot fix (THM-005).
- **Focus.** Every interactive element shows a visible ring in `--focus`, drawn as an `outline` (so it survives forced
  colors) outside the element with an offset, so it sits on the surface behind, never on the control's own fill; at
  least 3:1 against every surface it can sit on, never removed without a replacement, and never hidden behind a sticky header or toast (2.4.11): the toast region keeps to its corner, and the dashboard's shell has no fixed or sticky area at all (PP3.9), so it
  owes no scroll padding.
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
- **Tests.** Each primitive has axe tests in every state its specs cover, and is checked in both modes as drawn in the
  showcase (PP3.6). Each public
  option value has golden and axe tests in both modes (PP7, PP11), and each preset a manual keyboard and screen-reader
  check (THM-003).

## Naming

| What              | Rule                                                                                                                             | Example                                        |
| ----------------- | -------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------- |
| Library selectors | `pp-` elements in kebab-case, or a `pp-` attribute on the browser's own element; `pp` attribute directives in camelCase (ESLint) | `pp-field`, `button[pp-button]`, `ppTooltip`   |
| Library classes   | `Pp` before the name, so an export never collides with an app's own component                                                    | `PpButton`, `PpRadioOption`                    |
| App selectors     | `ppa-` in `pantrypress-admin`, `pps-` in `pantrypress-site` (ESLint)                                                             | `ppa-site-switcher`                            |
| Files             | kebab-case, one component per file, its spec beside it (or the spec of the primitive it is tested within)                        | `checkbox.ts`, `checkbox.spec.ts`              |
| Color tokens      | The role, never the hue, with the `--L-` and `--D-` pairs behind it                                                              | `--accent`, `--L-accent`                       |
| Scale tokens      | Family, then an ascending step                                                                                                   | `--space-3`, `--shadow-1`                      |
| Variants          | A typed input, reflected on the host as a `data-*` attribute that the styles select on; never `data-mode`                        | `variant="primary"` → `data-variant="primary"` |
| Public options    | The spec's attribute names on `.site`, unchanged                                                                                 | `data-header="split"`                          |

Tokens carry no `pp-` prefix: the public names are fixed by the spec, the shared names have to match them, and no
third-party CSS shares the pages.

## How the rules are kept

| Rule                                       | Kept by                                                                                                   |
| ------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| The entry points never cross               | ESLint in each app and in `themes/`; ng-packagr                                                           |
| `/themes` is server-safe and deterministic | `eslint.ssr-safety.js`; golden files (ARCH-005)                                                           |
| No literal color                           | Specs added with the tokens (PP3.2, PP7.4); `scripts/check-public-page.mjs` for the public page (PPL.6)   |
| Contrast and focus ring                    | A contrast spec over the token pairs in both modes (PP3.2, PPL.6); the server's contrast report (THM-005) |
| Accessibility                              | axe in component tests; axe with contrast over the showcase in Playwright, both modes (`npm run e2e`)     |
| Budget                                     | `npm run budget` (RND-007)                                                                                |

## The public page

PPL.6 fills this section (PP-D19): what a platform public page may draw itself, and what it must take from the library.

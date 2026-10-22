# PantryPress: Site Customization Spec

Version 1.2, 2026-10-22. Adopted by the owner as PP-D17, with sections and pages per PP-D18; the requirements (THM-001 to THM-013, RCP-010) and SCRUB phase PP11 now point here. Suggested repository home: `docs/requirements/pantrypress-customization-spec.md`.

Source mockups:

- Mockup 1, "light customization" (current requirements, THM-001 to THM-004): `pantrypress-post-mockup.html`
- Mockup 2, "Studio" (expanded personalization): `pantrypress-studio-mockup.html`

Both live in `/mnt/project-files/pantrypress/mockup/`. This spec defines every option in both mockups, with the allowed values, defaults, render contract, validation rules and acceptance tests. Claude Code should be able to rebuild both mockups' behavior from this spec in the real PantryPress stack (ASP.NET Core + Aspire, Angular 22 SSR, `@pantrypress/ui` with a `/themes` entry point).

> **Status.** Studio customization is adopted (PP-D17, 2026-10-22). Options marked **Studio** are in scope. Options marked **v1** describe the first mockup's smaller option set; under PP-D17 the v1 themes become presets and the v1-only fields (`theme`, `accentIndex`, `fontPair`, v1 `header` and `layout`) are not built. The `level` field stays so Starter can be limited to presets (section 15).

---

## 1. How to use this with Claude Code

1. Give Claude Code this file plus `pantrypress-requirements.md`.
2. Build in the order of SCRUB phase PP11 in `pantrypress-scrub-prompts.md` (section 17 here is the outline it was made from). Each prompt is one seam.
3. Use the two mockup HTML files as the visual reference. Their CSS shows the intended look for each value. Their JavaScript is a stand-in: in the product, all of this is computed on the server.
4. Treat every MUST in this file as an acceptance criterion.

---

## 2. Standards that apply to every option

| ID | Standard |
| --- | --- |
| CUS-STD-01 | **Typed settings only.** Every option is a field with an enum, bounded number, color or bounded string. There is no free-form CSS, HTML, JavaScript, template, font upload or plugin. |
| CUS-STD-02 | **Server is the source of truth.** Settings are validated in the API (FluentValidation or equivalent) and again when the theme snapshot is built. Invalid values are rejected with a plain-language message, never silently coerced, except the contrast auto-fix in section 5, which is by design. |
| CUS-STD-03 | **Render contract.** Settings render as (a) CSS custom properties on the `.site` root element and (b) `data-*` attributes on the same element. Components style themselves only through those tokens and attributes. No component reads settings directly. |
| CUS-STD-04 | **Precomputed tokens.** All derived colors (dark-mode accent, readable link color, ink on accent) are computed on the server when settings are saved and stored in the theme snapshot. The public page does no color math. |
| CUS-STD-05 | **Accessibility.** Every reachable combination meets WCAG 2.2 AA: body text 4.5:1, large text and UI components 3:1, visible focus, reduced motion respected. Section 5 enforces color; section 6 limits type sizes. |
| CUS-STD-06 | **Performance.** Customization adds no JavaScript to public pages. Total first-party CSS + JS stays under the 100 KB compressed budget (RND-007) for every preset. Fonts are self-hosted WOFF2, subset to Latin + Latin Extended, at most 2 families and 4 font files per page. |
| CUS-STD-07 | **Preview then apply.** Changes are drafts until applied. The admin preview renders the blogger's own latest post with the draft settings (THM-004). Apply writes a new immutable theme snapshot and purges the site's edge cache. |
| CUS-STD-08 | **Revert.** The last 20 applied snapshots are kept. The blogger can restore any of them, or reset to a preset. |
| CUS-STD-09 | **Light, dark, auto.** Every color token has a light and a dark value. `auto` follows the reader's `prefers-color-scheme`. |
| CUS-STD-10 | **Plan gating.** Options gated by plan (section 15) are hidden or read-only with an upgrade explanation. A downgrade never deletes settings; gated values fall back to defaults at render time and are restored on upgrade (BILL-004). |
| CUS-STD-11 | **Text sanitization.** All text options are plain text: trimmed, HTML-encoded on output, no markup, no URLs except the announcement link field, which must be `https://` or a site-relative path. |
| CUS-STD-12 | **Fixed reader features.** Jump to recipe, print, servings scaling, US/metric toggle, cook mode, breadcrumbs, affiliate disclosure and the consent banner cannot be turned off by any option. |

---

## 3. Settings data model

One `SiteThemeSettings` document per site, stored as JSON on the site's theme record with a `schemaVersion`. The API and the Angular admin share one schema.

### 3.1 TypeScript (Angular admin and SSR)

```ts
export type Mode = 'auto' | 'light' | 'dark';
export type ThemeId = 'market' | 'hearth' | 'larder';
export type Tone = 'white' | 'paper' | 'cool' | 'blush' | 'sage';
export type HeadingFont = 'bricolage-grotesque' | 'fraunces' | 'archivo' | 'playfair-display' | 'dm-serif-display' | 'caprasimo' | 'young-serif';
export type BodyFont = 'literata' | 'source-serif-4' | 'lora' | 'nunito-sans' | 'karla';
export type FontPair = 'theme' | 'serif' | 'sans';                 // v1 only
export type MarkShape = 'circle' | 'square' | 'stamp' | 'none';
export type ButtonStyle = 'solid' | 'pill' | 'outline' | 'shadow';
export type Texture = 'none' | 'grain' | 'dots' | 'gingham' | 'lines';
export type HeaderStyle = 'split' | 'centered' | 'banner' | 'stacked' | 'minimal';
export type HeroStyle = 'standard' | 'fullbleed' | 'overlay' | 'split';
export type SidebarPosition = 'right' | 'left' | 'none';
export type CardStyle = 'classic' | 'magazine' | 'index';
export type StepStyle = 'circle' | 'numeral' | 'plain';
export type PostModule = 'ad-after-card' | 'share' | 'signup-end' | 'reviews' | 'author-box';
export type RecipeBadge = 'kitchen-tested' | 'freezer-friendly' | 'one-bowl' | 'kid-favorite'
  | 'make-ahead' | 'gluten-free' | 'dairy-free' | 'vegan' | 'under-30-min' | 'budget';

export interface SiteThemeSettings {
  schemaVersion: 1;
  level: 'v1' | 'studio';            // which option set this site uses (plan + feature flag)
  presetId: string | null;           // last preset applied; null once anything is edited
  mode: Mode;

  // v1
  theme: ThemeId;
  accentIndex: 0 | 1 | 2;            // index into the theme's accessible accent pairs
  fontPair: FontPair;
  header: 'split' | 'centered' | 'minimal';   // v1 subset of HeaderStyle
  layout: 'sidebar' | 'single';
  logoMediaId: string | null;
  faviconMediaId: string | null;

  // Studio (ignored when level = 'v1')
  brand: { siteName: string; tagline: string; monogram: string; markShape: MarkShape };
  color: { brand: string; highlight: string; tone: Tone };   // brand/highlight: '#rrggbb'
  type: { heading: HeadingFont; body: BodyFont; basePx: number; headingScalePct: number; dropCap: boolean };
  shape: { radiusPx: number; imageRadiusPx: number; buttons: ButtonStyle; texture: Texture };
  header2: { style: HeaderStyle; announcement: { enabled: boolean; text: string; href: string | null } };
  post: { hero: HeroStyle; sidebar: SidebarPosition; measurePx: number };
  card: { style: CardStyle; steps: StepStyle; showChefNote: boolean; jumpLabel: string };
  sections: {
    order: PostModule[];                                   // all 5, each exactly once
    hidden: PostModule[];                                  // subset of order
    gallery: boolean; inlineSignup: boolean; ads: boolean; relatedRow: boolean;
  };
  personal: { signOff: { enabled: boolean; text: string }; newsletterHeading: string; newsletterButton: string };
}
```

Per-recipe options (badges, chef note text) live on the recipe, not the site: `Recipe.badges: RecipeBadge[]` (max 4) and `Recipe.chefNote: string | null` (max 280 chars). The site setting `card.showChefNote` only controls whether the note renders.

### 3.2 C# (API)

Mirror the TypeScript as `record` types under `Modules/Sites/Domain/Theme/`. Enums serialize as kebab-case strings (`JsonStringEnumConverter` with a kebab-case naming policy). Validation lives in `SiteThemeSettingsValidator`.

### 3.3 Theme snapshot (what the renderer reads)

```ts
export interface ThemeSnapshot {
  id: string; siteId: string; createdUtc: string; settingsHash: string;
  attributes: Record<string, string>;            // every data-* attribute for .site
  tokens: { light: Record<string, string>; dark: Record<string, string>; shared: Record<string, string> };
  fonts: { heading: FontFace; body: FontFace; extra: FontFace[] };  // extra = Caveat when needed
  text: { siteName: string; tagline: string; monogram: string; announcement?: { text: string; href?: string };
          signOff?: string; newsletterHeading: string; newsletterButton: string; jumpLabel: string };
  sections: { order: PostModule[]; hidden: PostModule[]; gallery: boolean; inlineSignup: boolean; ads: boolean; relatedRow: boolean };
  contrastReport: ContrastReport;                // section 5.5
}
```

SSR writes `tokens.shared` and `tokens.light` as inline custom properties on `.site`, writes `tokens.dark` inside a generated `<style>` block (see 4.3), and sets `attributes` on `.site`.

---

## 4. Token contract

### 4.1 Color tokens

Each token exists as `--L-<name>` (light) and `--D-<name>` (dark). The active `--<name>` is chosen by mode.

| Token | Meaning | Used for |
| --- | --- | --- |
| `bg` | Page background | body, cards that sit on the page |
| `surface` | Raised or tinted panels | callouts, recipe tools bar, related row, rating box |
| `ink` | Body text | all running text |
| `muted` | Secondary text | captions, meta, labels (must still pass 4.5:1 on `bg` and `surface`) |
| `line` | Borders and dividers | 1px rules, input borders |
| `accent` | Brand fill | buttons, recipe card border, step circles, signup band, banner header |
| `accent-ink` | Text on `accent` | button labels, text in banner header and signup band |
| `accent-text` | Brand color used as text on `bg` | links, tip labels, big numerals, sign-off |
| `hl` | Highlight | stars, badges, announcement bar, avatar |
| `hl-ink` | Text on `hl` | announcement text, badge text |

v1 themes predate `accent-text` and `hl-ink`. In v1, set `accent-text = accent` and `hl-ink = #2a1d05`; the v1 accent pairs in 7.2 were picked to pass without them.

### 4.2 Shared tokens

| Token | Type | Studio source | v1 source |
| --- | --- | --- | --- |
| `--f-display` | font stack | `type.heading` (section 6) | theme or `fontPair` |
| `--f-body` | font stack | `type.body` | theme or `fontPair` |
| `--display-weight` | number | from heading font registry | theme |
| `--display-track` | em | from heading font registry | theme |
| `--base` | px | `type.basePx` | fixed 17px (16px under 560px container width) |
| `--hscale` | unitless | `type.headingScalePct / 100` | 1 |
| `--r` | px | `shape.radiusPx` | theme (Market 10, Hearth 18, Larder 2) |
| `--img-r` | px | `shape.imageRadiusPx` | same as `--r` |
| `--measure` | px | `post.measurePx` | 720px |

### 4.3 Mode resolution (CSS)

```css
.site { --bg: var(--L-bg); /* …every color token… */ }
.site[data-mode="dark"] { --bg: var(--D-bg); /* … */ color-scheme: dark; }
@media (prefers-color-scheme: dark) { .site[data-mode="auto"] { --bg: var(--D-bg); /* … */ color-scheme: dark; } }
```

The mockups also listen to the artifact viewer's `data-theme` attribute. The product does not need that; only `prefers-color-scheme` matters.

### 4.4 Data attributes on `.site`

`data-mode`, `data-theme-name` (v1), `data-fonts` (v1), `data-header`, `data-layout`, `data-hero`, `data-card`, `data-steps`, `data-btn`, `data-texture`, `data-mark`, and class `dropcap` when `type.dropCap` is true.

`data-layout` values are `right | left | single` in Studio. v1 `sidebar` maps to `right`.

---

## 5. Color rules (Studio)

### 5.1 Inputs

- `color.brand`: any `#rrggbb`. Default `#2f6b3a`.
- `color.highlight`: any `#rrggbb`. Default `#f2c94c`.
- `color.tone`: one of the five tones below. Default `white`.

Quick swatches offered in the admin (not limits):
brand `#2f6b3a #1f5f7a #8a2f4b #c2410c #6d28d9 #d9a400 #0f766e`; highlight `#f2c94c #ffb4a2 #9ad0ec #c8e6a0 #f6a5c0`.

### 5.2 Tones (fixed neutrals)

| Tone | Mode | bg | surface | ink | muted | line |
| --- | --- | --- | --- | --- | --- | --- |
| white | L | `#ffffff` | `#f4f4f1` | `#1c1c1c` | `#5c5c5c` | `#e2e2de` |
| white | D | `#131313` | `#1d1d1d` | `#ececec` | `#a3a3a3` | `#2e2e2e` |
| paper | L | `#fbf7ef` | `#f2ead9` | `#2b2318` | `#6b5d48` | `#e6dcc8` |
| paper | D | `#17140f` | `#221d16` | `#efe8dc` | `#b3a68f` | `#363024` |
| cool | L | `#f5f8fb` | `#e8eff6` | `#152230` | `#4f6173` | `#d6e0ea` |
| cool | D | `#0f141a` | `#18202a` | `#e6edf5` | `#9eb0c2` | `#2a3542` |
| blush | L | `#fdf6f5` | `#f7e6e3` | `#2d1a1a` | `#6e5250` | `#ecd6d2` |
| blush | D | `#1a1213` | `#251a1c` | `#f4e8e8` | `#bba2a2` | `#3b2a2c` |
| sage | L | `#f4f7f1` | `#e6eedf` | `#1b2618` | `#55654f` | `#d6e1cd` |
| sage | D | `#11160f` | `#1a2117` | `#e7eee3` | `#a1b09a` | `#2c3828` |

Each tone MUST be verified by a unit test: `ink` and `muted` at least 4.5:1 on both `bg` and `surface`, in both modes.

### 5.3 Math (WCAG 2.x)

```
channel(c) = c/255 <= 0.03928 ? (c/255)/12.92 : ((c/255 + 0.055)/1.055)^2.4
L(hex)     = 0.2126 R + 0.7152 G + 0.0722 B          (channels linearized)
ratio(a,b) = (max(La,Lb) + 0.05) / (min(La,Lb) + 0.05)
mix(a,b,t) = per-channel a + (b − a)·t, rounded, clamped 0–255
towards(c, target, bg, min):
    t = 0; out = c
    while ratio(out, bg) < min and t < 1: t += 0.05; out = mix(c, target, t)
    return out
inkOn(c)   = ratio(c, #ffffff) >= ratio(c, #141414) ? #ffffff : #141414
```

### 5.4 Derived tokens

| Token | Light | Dark |
| --- | --- | --- |
| `accent` | `brand` | `towards(brand, #ffffff, D.bg, 3.0)` |
| `accent-ink` | `inkOn(L.accent)` | `inkOn(D.accent)` |
| `accent-text` | `towards(brand, #000000, L.bg, 4.5)` | `towards(brand, #ffffff, D.bg, 4.5)` |
| `hl` | `highlight` | `highlight` |
| `hl-ink` | `inkOn(highlight)` | `inkOn(highlight)` |

Rules:

- `accent` as a fill must reach 3:1 against `bg` (UI component contrast). If the light `brand` fails 3:1 on `L.bg`, accept it as a fill but note it in the contrast report; borders and buttons still pass because `accent-ink` on `accent` is checked at 4.5:1.
- If `inkOn(accent)` still gives less than 4.5:1 (mid-tone colors such as `#888888`), the save is rejected with: "This brand color is too close to mid-grey for readable button text. Pick a darker or lighter shade." The admin suggests the nearest passing shade by running `towards` in both directions.
- Same rule for `highlight` with `hl-ink`.

### 5.5 Contrast report

Stored on the snapshot and shown next to the color picker:

```ts
interface ContrastReport {
  brandOnBg: number;            // e.g. 5.8
  linkAdjusted: boolean;        // accent-text != brand in light mode
  darkLinkAdjusted: boolean;
  accentInk: number; hlInk: number;
  status: 'pass' | 'auto-fixed' | 'rejected';
}
```

Admin display: `AA 5.8:1` (green) when it passes untouched, or `3.1:1 · text auto-darkened` (amber) when `accent-text` was adjusted.

---

## 6. Typography

### 6.1 Font registry

All self-hosted WOFF2 from the `@pantrypress/ui/themes/fonts` package (OFL-licensed, from Google Fonts sources). Only the weights listed ship.

| Key | Family | Role | Weights | `--display-weight` | `--display-track` | Fallback |
| --- | --- | --- | --- | --- | --- | --- |
| bricolage-grotesque | Bricolage Grotesque | heading | 500, 700, 800 | 800 | -0.02em | system-ui, sans-serif |
| fraunces | Fraunces | heading | 500, 700 | 700 | -0.01em | Georgia, serif |
| archivo | Archivo | heading | 500, 700, 800 | 800 | -0.025em | "Helvetica Neue", Arial, sans-serif |
| playfair-display | Playfair Display | heading | 700, 800 | 800 | -0.01em | Georgia, serif |
| dm-serif-display | DM Serif Display | heading | 400 | 400 | 0 | Georgia, serif |
| caprasimo | Caprasimo | heading | 400 | 400 | 0 | Georgia, serif |
| young-serif | Young Serif | heading | 400 | 400 | -0.01em | Georgia, serif |
| literata | Literata | body | 400, 600 | — | — | Georgia, serif |
| source-serif-4 | Source Serif 4 | body | 400, 600 | — | — | Georgia, serif |
| lora | Lora | body | 400, 600 | — | — | Georgia, serif |
| nunito-sans | Nunito Sans | body | 400, 700 | — | — | system-ui, sans-serif |
| karla | Karla | body | 400, 700 | — | — | system-ui, sans-serif |
| caveat | Caveat | accent only | 600 | — | — | "Segoe Script", cursive |

Caveat loads only when the sign-off is on or the card style is `index`. Preload only the heading font's display weight and the body 400 file.

### 6.2 Options

| Option | Type | Range / values | Default | Level |
| --- | --- | --- | --- | --- |
| `type.heading` | enum | 7 heading keys | bricolage-grotesque | Studio |
| `type.body` | enum | 5 body keys | literata | Studio |
| `type.basePx` | int | 15–20, step 1 | 17 | Studio |
| `type.headingScalePct` | int | 85–130, step 5 | 100 | Studio |
| `type.dropCap` | bool | | false | Studio |
| `fontPair` | enum | theme, serif (Fraunces + Source Serif 4), sans (Archivo + Nunito Sans) | theme | v1 |

### 6.3 Type scale

| Element | Size |
| --- | --- |
| Body | `var(--base)`, line-height 1.65; drops to 16px when `basePx` > 16 and the container is under 560px |
| Post title | `clamp(30px, 5.2cqi, 50px) × --hscale` |
| Overlay hero title | `clamp(30px, 5.6cqi, 60px) × --hscale` |
| Split hero title | `clamp(30px, 4.6cqi, 52px) × --hscale` |
| H2 in post | `28px × --hscale` |
| Recipe card title | `30px × --hscale` (index card: Caveat 44px, not scaled) |
| Masthead header wordmark | `clamp(40px, 9cqi, 92px)` |
| Banner header wordmark | `clamp(32px, 6cqi, 56px)` |

Headings use `text-wrap: balance`. Drop cap: first paragraph of the post body, display font, `3.6em`, floated, color `accent-text`.

---

## 7. Theme presets and v1 themes

### 7.1 v1 themes (THM-001)

| Theme | Heading / body | `--r` | Light bg / surface / ink / muted / line | Dark bg / surface / ink / muted / line |
| --- | --- | --- | --- | --- |
| Market | Bricolage Grotesque 800 / Literata | 10px | `#ffffff #f2f6f0 #1b231c #586a5b #dce4d9` | `#121712 #1b221c #e8eee6 #9fb0a0 #2c372d` |
| Hearth | Fraunces 700 / Nunito Sans | 18px | `#fbf8f6 #f3ebe7 #2a1d22 #6e5a61 #e8dcd6` | `#1a1215 #251a1f #f1e7ea #b9a3aa #3b2a31` |
| Larder | Archivo 800 / Source Serif 4 | 2px | `#f6f6f3 #ffffff #14161f #555a6e #d9dbe2` | `#0f1118 #181b25 #eceef5 #a2a7bb #2b2f3d` |

Highlight per theme: Market `#f2c94c` / `#e0b93f`, Hearth `#e8a33d`, Larder `#f4c430`.

Theme names are placeholders until THM-001's DECIDE is closed.

### 7.2 v1 accent pairs (`accentIndex`)

Each entry: light accent, dark accent, light accent-ink, dark accent-ink.

| Theme | 0 | 1 | 2 |
| --- | --- | --- | --- |
| Market | `#2f6b3a #7cc48a #fff #0d1a10` | `#1f5f7a #7cc3e0 #fff #0b1d26` | `#7a4b12 #e2b06a #fff #24170a` |
| Hearth | `#8a2f4b #e48aa6 #fff #2a0f19` | `#9b3a1c #f09a77 #fff #2a1108` | `#3d5a2a #a7cf8a #fff #132008` |
| Larder | `#2a3f8f #a3b3ff #fff #0f1430` | `#0f6b62 #6fd8cb #fff #062421` | `#8c1d2c #ff9aa6 #fff #2b070c` |

### 7.3 Studio presets

A preset writes every listed field and sets `presetId`. Any later edit sets `presetId = null`. Fields not listed keep their current values (text, sections, personal touches).

| Preset | brand | highlight | tone | heading / body | radius / image radius | buttons | texture | header | hero | sidebar | card | steps | mark | heading scale |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Market | `#2f6b3a` | `#f2c94c` | white | Bricolage / Literata | 10 / 10 | solid | none | split | standard | right | classic | circle | circle | 100 |
| Hearth | `#8a2f4b` | `#f6a5c0` | blush | Fraunces / Nunito Sans | 18 / 24 | pill | none | centered | standard | none | classic | circle | circle | 105 |
| Larder | `#1f3f9a` | `#f2c94c` | cool | Archivo / Source Serif 4 | 0 / 0 | shadow | lines | stacked | split | right | magazine | numeral | none | 115 |
| Farmhouse | `#b23a2e` | `#f2c94c` | paper | Young Serif / Lora | 6 / 4 | solid | gingham | banner | overlay | right | index | plain | stamp | 105 |
| Sunny | `#c2410c` | `#ffd166` | white | Caprasimo / Karla | 24 / 32 | pill | dots | banner | fullbleed | none | magazine | numeral | square | 110 |
| Supper Club | `#0f766e` | `#e9b872` | sage | DM Serif Display / Source Serif 4 | 2 / 2 | outline | grain | centered | split | left | classic | numeral | stamp | 120 |

Every preset MUST pass the full accessibility and performance test matrix (section 18) in both modes.

---

## 8. Brand and logo

| Option | Type | Rules | Default | Level |
| --- | --- | --- | --- | --- |
| `brand.siteName` | string | 1–60 chars | site name from signup | Studio (v1 uses the site record's name) |
| `brand.tagline` | string | 0–120 chars; empty hides it | "" | v1 + Studio |
| `brand.monogram` | string | 1–3 chars, letters, digits or `&` | initials of site name | Studio |
| `brand.markShape` | enum | circle, square, stamp, none | circle | Studio |
| `logoMediaId` | media | PNG, SVG (sanitized), WebP; at most 1200×400; when set it replaces the monogram mark | null | v1 |
| `faviconMediaId` | media | square, at least 512×512; generates 32, 180 and 512 sizes | monogram rendered on `accent` | v1 |

Mark rendering: 44px. Circle = `border-radius: 50%`, filled `accent`, text `accent-ink`. Square = `border-radius: calc(var(--r) × 0.6)`. Stamp = transparent with a double ring (`inset 0 0 0 2px accent, inset 0 0 0 5px bg, inset 0 0 0 6px accent`) and text in `accent-text`. None = wordmark only.

---

## 9. Shape and surface

| Option | Type | Range / values | Default | Level |
| --- | --- | --- | --- | --- |
| `shape.radiusPx` | int | 0–28 | 10 | Studio |
| `shape.imageRadiusPx` | int | 0–40, step 2 | 10 | Studio |
| `shape.buttons` | enum | solid, pill, outline, shadow | solid | Studio |
| `shape.texture` | enum | none, grain, dots, gingham, lines | none | Studio |

Buttons:

- **solid:** fill `accent`, text `accent-ink`, radius `--r`.
- **pill:** solid, but radius 999px (also applies to the servings and units pills).
- **outline:** primary buttons become transparent with a 1.5px `accent` border and `accent-text` label.
- **shadow** ("Offset" in the UI): 1.5px `ink` border and `box-shadow: 3px 3px 0 var(--ink)`.

Buttons inside the signup band and banner header always use inverted colors (`accent-ink` fill, `accent` text) whatever the button style.

Textures are CSS backgrounds on `.site`, built only from tokens so they follow the mode:

| Texture | CSS |
| --- | --- |
| grain | two `radial-gradient(color-mix(in srgb, var(--ink) 9% / 6%, transparent) 0.6px, transparent 0.8px)` layers at 7px and 11px, offset 3px 5px |
| dots | `radial-gradient(var(--line) 1.2px, transparent 1.4px)` at 18px |
| gingham | two crossing `linear-gradient(color-mix(in srgb, var(--accent) 7%, transparent) 50%, transparent 50%)` at 28px |
| lines | `repeating-linear-gradient(0deg, transparent 0 31px, var(--line) 31px 32px)` |

Textures apply to the page background only. Header, footer, recipe card, signup, callouts and inputs keep solid backgrounds so text never sits on a pattern.

---

## 10. Header

| Option | Type | Values | Default | Level |
| --- | --- | --- | --- | --- |
| `header` (v1) | enum | split, centered, minimal | split | v1 |
| `header2.style` | enum | split, centered, banner, stacked, minimal | split | Studio |
| `header2.announcement.enabled` | bool | | false | Studio |
| `header2.announcement.text` | string | 1–120 chars when enabled | "" | Studio |
| `header2.announcement.href` | url or null | `https://` or `/path` | null | Studio |

Header styles:

- **split:** mark and wordmark on the left, nav on the right, wrapping under 560px.
- **centered:** mark above a 36px wordmark, tagline below, nav centered under a top rule.
- **banner** (Studio): full-width `accent` band, 34px top padding, large wordmark, everything in `accent-ink`.
- **stacked** ("Masthead" in the UI, Studio): very large wordmark, no mark, nav between 2px `ink` rules with items spread across the width.
- **minimal:** small wordmark, no mark or tagline, nav collapses into a "Menu" button at every width.

Nav items come from the site menu (pages and categories, max 7 top-level), followed by the search pill. Announcement bar: sits above the header, `hl` background, `hl-ink` text, centered, 14px display font, with a trailing arrow when it has a link.

---

## 11. Post layout

| Option | Type | Values | Default | Level |
| --- | --- | --- | --- | --- |
| `layout` (v1) | enum | sidebar, single | sidebar | v1 |
| `post.sidebar` | enum | right, left, none | right | Studio |
| `post.hero` | enum | standard, fullbleed, overlay, split | standard | Studio |
| `post.measurePx` | int | 600–860, step 20 | 720 | Studio |

Hero styles:

- **standard:** the featured image sits in the text column under the post header.
- **fullbleed:** the featured image spans the page width under the site header, max 420px tall, cropped around the focal point (MED-003). The title stays in the column.
- **overlay:** full-width image, max 520px tall, with title and dek on a bottom gradient (`transparent → rgba(10,8,6,0.78)`), text white. The column header hides its title and dek. The renderer MUST check white text against the darkest part of the gradient over the image's focal area and fall back to `split` if it is under 4.5:1.
- **split:** two columns on `surface`: title and dek on the left, image on the right; stacks under 700px container width.

Sidebar: 280px column, right or left; drops below the article under 860px container width (left sidebar moves after the article). The sidebar holds a fixed widget set: about card, search, most popular, and the Pro ad slot.

Breakpoints are container queries on `.site` (`container-type: inline-size`), so the preview pane and real pages behave the same.

---

## 12. Recipe card

| Option | Type | Values | Default | Level |
| --- | --- | --- | --- | --- |
| `card.style` | enum | classic, magazine, index | classic | Studio |
| `card.steps` | enum | circle, numeral, plain | circle | Studio |
| `card.showChefNote` | bool | | true | Studio |
| `card.jumpLabel` | string | 2–24 chars | "Jump to recipe" | Studio |
| `Recipe.badges` | RecipeBadge[] | max 4, closed list | [] | Studio (per recipe) |
| `Recipe.chefNote` | string | 0–280 chars | null | Studio (per recipe) |

Card styles:

- **classic:** 2px `accent` border, radius `--r + 4px`, white `bg`.
- **magazine:** no border, soft shadow, header band in `accent` with `accent-ink` text, inverted buttons, times row on `surface`.
- **index:** 1px `line` border, radius 4px, ruled lines (`accent` at 16%, every 28px), a strip of "tape" in `hl` at 70% across the top center, red top rule under the header, Caveat title.

Step styles:

- **circle:** 26px `accent` circle with `accent-ink` number.
- **numeral:** 34px display-font numeral in `accent-text`, no fill.
- **plain:** "1." in `muted`, 15px.

Fixed parts of every card style, not configurable: title, summary, rating, print, rate, image, times row (prep, cook, additional, total, yield), servings ½×/1×/2×/3×, US/metric toggle, cook mode, ingredient groups, steps, equipment, notes, nutrition with disclaimer (RCP-006), course, cuisine, diet and keywords. JSON-LD output is the same for every style (RCP-008).

Badge labels: Kitchen-tested, Freezer-friendly, One bowl, Kid favorite, Make-ahead, Gluten-free, Dairy-free, Vegan, Under 30 minutes, Budget. Rendered as uppercase pills in `hl` and `hl-ink`. Diet badges (Gluten-free, Dairy-free, Vegan) are blogger claims and also set `suitableForDiet` in JSON-LD only when the recipe's diet field agrees.

---

## 13. Sections and order

### 13.1 Modules after the recipe card

| Module | Label | Default visible | Rules |
| --- | --- | --- | --- |
| ad-after-card | After-card ad | yes | Renders only when ads are on and the plan allows ads |
| share | Share buttons | yes | Plain links, no third-party scripts |
| signup-end | End-of-post signup | yes | Double opt-in form (ENG-005) |
| reviews | Ratings & comments | yes | Can be moved, not hidden, when the post has a recipe (ratings feed AggregateRating) |
| author-box | About the author box | no | Uses the author's profile |

Default order: ad-after-card, share, signup-end, reviews, author-box. `order` must contain all five exactly once.

### 13.2 Other section toggles

| Option | Default | Notes |
| --- | --- | --- |
| `sections.gallery` | true | Hides gallery blocks in the post body site-wide |
| `sections.inlineSignup` | true | Inline signup placed after the 4th block of the body |
| `sections.ads` | true | Master switch for all ad slots; ignored on Starter (no ads) |
| `sections.relatedRow` | true | "More from this category" row, 4 cards |

Ad placement rules (MON-002): in-content slot after the 2nd block, after-card slot, sidebar slot (Pro). No ad slot ever goes inside the recipe card, ingredients or steps, or directly above the jump button.

The same section types build the homepage (THM-002): featured recipe, latest posts, category rows, about strip and newsletter. Homepage section ordering uses the same order/hidden model.

---

### 13.3 Section library (homepage and site pages, PP-D18)

The homepage is an ordered list of these sections. Site pages can embed them as blocks. Sections have typed
options only and inherit all design settings; there are no per-section colors, fonts, or columns.

| Section | Options (type, limit) | Data source | Default on new homepage |
| --- | --- | --- | --- |
| featured-recipe | heading (≤60), recipe (picker), image side (left, right) | one published recipe post | 1st |
| latest-posts | heading (≤60), count (3–12, default 6), layout (grid, list), show excerpt (bool) | newest published posts, paginated on the homepage | 2nd |
| category-row | heading (≤60, defaults to category name), category (picker), count (3–8, default 4) | newest posts in the category | 3rd, for the top category |
| recipe-grid | heading (≤60), filter (category, tag, or badge), count (4–12, default 8), sort (newest, top rated) | published recipe posts matching the filter | — |
| about-strip | heading (≤60), text (≤300), photo (media), button label (≤24) to the About page | site settings and the About page | 4th |
| newsletter | uses the site's newsletter heading and button text; intro (≤140) | signup form slot (ENG-005) | 5th |
| start-here | heading (≤60), up to 6 links, each a post, page, or category with an optional label (≤40) | linked items | — |

Limits: at most 12 sections on the homepage and 8 section blocks per page. Each section has a visibility
switch. Data-backed sections are part of page data and rebuilt after a publish (SITE-011).

### 13.4 Page blocks and templates

Page-only blocks (site pages, not posts; POST-011):

| Block | Fields | Rules |
| --- | --- | --- |
| intro | image (media, focal point), heading (≤80), text (≤600, paragraphs and links), optional button (label ≤24, link) | Image left or right on wide screens, stacked on narrow |
| faq | up to 20 pairs: question (≤160), answer (≤1,000, paragraphs and links) | Rendered as native `details` elements; `FAQPage` JSON-LD off by default |
| social-links | none, reads site settings | Plain links, no third-party scripts |
| contact-form | intro (≤200) | At most one per page; ENG-007 delivery and spam controls |

Templates (SITE-012). Placeholders are marked `[[like this]]` and block publishing until replaced.

| Template | Blocks |
| --- | --- |
| About | intro (author photo, "Hi, I'm [[first name]]", [[two sentences about you and your kitchen]]), heading "Why I started this blog", paragraph placeholder, recipe-grid (top rated, 4), newsletter, social-links |
| Start here | paragraph ("New here? Start with these."), start-here (6 placeholder links), category-row for each of the top 3 categories, newsletter |
| Contact | intro ("Say hello"), contact-form, faq (3 starter questions about substitutions, sponsorships, and recipe use), social-links |

## 14. Personal touches

| Option | Type | Rules | Default |
| --- | --- | --- | --- |
| `personal.signOff.enabled` | bool | | false |
| `personal.signOff.text` | string | 1–40 chars | "Happy baking, {first name}" |
| `personal.newsletterHeading` | string | 1–80 chars | "Get new recipes in your inbox" |
| `personal.newsletterButton` | string | 1–24 chars | "Subscribe" |

Sign-off renders at the end of the post body, before the recipe card, in Caveat 600 at 30px, `accent-text`, rotated −2deg. Newsletter heading and button text apply to all three signup forms (inline, end-of-post, footer).

---

## 15. Plan gating

| Capability | Starter | Plus | Pro |
| --- | --- | --- | --- |
| v1 options | 2 themes (Market, Hearth) | All | All |
| Studio options (if PP-D17 adopted) | Presets only | All except textures and stacked/banner headers (DECIDE) | All |
| Custom domain | No | Yes | Yes |
| Ads master switch | Hidden | AdSense slots | AdSense or allow-listed network, plus sidebar slot |
| "Made with PantryPress" badge | Required | Removable | Removable |

The Studio split between Plus and Pro is a proposal; record it with PP-D17.

---

## 16. Decision PP-D17: Studio customization (adopted 2026-10-22)

The Studio option set is adopted as typed settings, replacing THM-002's "light customization" wording with:

> THM-002 (revised): Customization through typed settings only: presets, brand color with enforced contrast, highlight color, background tone and texture, curated self-hosted fonts and scale, corner radius, button style, header style and announcement bar, hero style, sidebar position, text width, recipe card and step style, recipe badges, section toggles and order, and short personal text fields. No custom CSS, HTML, templates, scripts or font uploads.

Cost: 11 more SCRUB prompts (PP11 grew from 8 to 19), a larger accessibility test matrix, and a theme snapshot builder. Risk: more combinations to keep under the performance budget; mitigated by the snapshot tests in section 18.

---

## 17. Suggested SCRUB prompts

Each is one seam, plan first, verified before marking done.

1. **Settings schema.** `SiteThemeSettings` records, enums, defaults, validator, JSON column with `schemaVersion`, migration.
2. **Color engine.** Pure functions for section 5 in C# with unit tests (tones, derived tokens, rejection cases, report).
3. **Font registry.** Self-hosted WOFF2 package, registry table from 6.1, `@font-face` generation, preload rules.
4. **Snapshot builder.** Settings → `ThemeSnapshot` (tokens, attributes, fonts, text, sections, report), with hashing and the 20-snapshot history.
5. **SSR token injection.** Angular SSR writes tokens, the dark `<style>` block and attributes on `.site`; edge-cache purge on apply.
6. **Base theme CSS.** Token-driven component styles in `@pantrypress/ui/themes` for v1 themes and accent pairs.
7. **Header variants.** Five header styles and the announcement bar.
8. **Post layout.** Sidebar positions, measure and four hero styles with the overlay contrast fallback.
9. **Recipe card styles.** Three card styles, three step styles, badges, chef note, jump label.
10. **Shape and surface.** Radius, image radius, button styles, textures.
11. **Typography options.** Heading and body choice, base size, heading scale, drop cap.
12. **Sections.** Module order and visibility, toggles, ad placement rules.
13. **Personal touches.** Sign-off and newsletter copy.
14. **Presets.** Six presets from 7.3, apply and reset.
15. **Admin customizer.** Ten collapsible groups in this order: Starting look, Brand and logo, Color, Typography, Shape and surface, Header, Post layout, Recipe card, Sections and order, Personal touches. Live preview of the blogger's latest post, contrast badge, Apply, Discard, Revert to snapshot.
16. **Plan gating.** Entitlement checks per section 15 in API and admin.
17. **Test matrix.** Section 18 automated in CI.

---

## 18. Acceptance tests

| ID | Test |
| --- | --- |
| CUS-T01 | Every enum value of every option renders without layout overflow at container widths 360, 600, 900 and 1280px (Playwright screenshot plus `scrollWidth` check). |
| CUS-T02 | All six presets × 2 modes pass axe-core with zero serious or critical violations on a post page. |
| CUS-T03 | Color engine: for 500 random brand colors and every tone, either `status = rejected` or every derived pair meets its threshold (accent-text 4.5, accent-ink 4.5, hl-ink 4.5, dark accent 3.0). |
| CUS-T04 | Mid-grey brand `#888888` is rejected with the documented message and a suggested passing shade. |
| CUS-T05 | Text options reject markup and over-length input; output is HTML-encoded. |
| CUS-T06 | `sections.order` with a missing or duplicate module is rejected. |
| CUS-T07 | Overlay hero over a light focal area falls back to split. |
| CUS-T08 | Each preset's post page stays under the 100 KB first-party budget and loads at most 4 font files. |
| CUS-T09 | Recipe JSON-LD is byte-identical across all card and step styles for the same recipe. |
| CUS-T10 | No ad slot renders inside `.recipe` for any setting combination. |
| CUS-T11 | Downgrading Pro → Starter hides gated options at render time and restores them after re-upgrade with no data loss. |
| CUS-T12 | Apply creates a new snapshot, purges the site's edge cache, and Revert restores the prior snapshot exactly (same `settingsHash`). |
| CUS-T13 | Jump to recipe, print, scaling, units and cook mode are present for every setting combination. |
| CUS-T14 | Every section type and page block renders without overflow at the four widths, passes axe in every preset, and inherits design tokens (no literal colors in its CSS). |
| CUS-T15 | A page built from the About template cannot be published until every `[[placeholder]]` is replaced. |

---

## 19. Open decisions

- Which Studio options are Plus versus Pro (default in section 15).
- Final theme names (THM-001) and whether v1 themes become Studio presets once PP-D17 is adopted.
- Whether `reviews` may be hidden on posts without a recipe card.
- Logo upload: allow SVG (needs a strict sanitizer) or raster only.

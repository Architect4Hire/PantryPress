# 0017 — Studio site customization

- **Status:** Accepted, with open items
- **Date:** 2026-10-22
- **Decided by:** Owner
- **Decision ID:** PP-D17
- **Supersedes:** the "light customization only" rule of requirements versions before v2.1
- **Requirements:** THM-001 to THM-013, RCP-010, RND-007, CMP-006; requirements section 1.2 (Site design)
- **Rules affected:** .claude/rules/sites.md, .claude/rules/design-system.md, .claude/rules/rendering.md
- **Detail:** [customization spec](../requirements/pantrypress-customization-spec.md)

## Context

Bloggers want a site that looks like theirs. Theme hunting and plugin breakage on WordPress are the pain
PantryPress replaces. With "light customization only", every PantryPress site looked alike. Free-form styling
would let any blogger break accessibility, blow the performance budget, and create support load no single
operator can carry.

## Decision

Every design option is a typed, validated setting stored in a versioned `SiteThemeSettings` document.

On apply, the server builds an immutable **theme snapshot**:

- light and dark CSS custom properties;
- `data-*` layout attributes;
- font selections;
- sanitized text;
- section order;
- a contrast report.

SSR writes the snapshot onto the site root. Components style themselves only through those tokens and
attributes, and public pages run no customization JavaScript.

Brand colors are free choice. The server corrects derived text and fill colors to WCAG AA and refuses colors
that cannot be fixed. Fonts come from a curated self-hosted registry. The six launch looks are **presets**
over the same option set, so one component tree serves every site.

Still excluded: custom CSS, HTML, templates, scripts, font uploads, and third-party themes.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Typed settings, presets, server-built snapshot (chosen) | 11 more prompts (PP11 grew from 8 to 19), a theme snapshot builder, and a larger accessibility test matrix. |
| Light customization only (previous rule) | Fewer combinations to test, but sites look alike and the product promise is weaker. |
| A custom CSS field | Maximum freedom, but no WCAG or budget guarantee, upgrades break sites, and support load grows. |
| Separate codebases per theme | Distinct looks, but six times the components, golden files, and accessibility work. |

## Consequences

- The v1 option fields (`theme`, `accentIndex`, `fontPair`, v1 `header` and `layout`) are not built.
- 20 snapshots are kept per site. Revert restores one exactly (same `settingsHash`), and apply purges the
  site's edge cache.
- Gated options go through the entitlements facade. A downgrade keeps settings and renders defaults (THM-012).
- Every option value has golden and axe tests in both modes, and every preset stays inside RND-007 (CUS-T01
  to CUS-T13).

## Open items

- **D-17:** which Studio options Plus gets versus Pro. Default assumed: Plus gets all presets and options
  except textures and the Banner and Masthead (`stacked`) headers; Pro gets all. Closes at PP11.
- **THM-001:** final preset names and looks. Default assumed: Market, Hearth, Larder, Farmhouse, Sunny,
  Supper Club. Market closes at PP7, the rest at PP11.
- **THM-002:** logo formats. Default assumed: raster only; SVG only with a strict sanitizer. Closes at PP11.
- **THM-010:** hiding ratings and comments on posts without a recipe. Default assumed: yes. Closes at PP11.

See the [register](pantrypress.md#3-open-decide-items).

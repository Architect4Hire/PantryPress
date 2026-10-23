# 0018 — Section-based pages, no page builder

- **Status:** Accepted, with one open item
- **Date:** 2026-10-22
- **Decided by:** Owner
- **Decision ID:** PP-D18
- **Requirements:** SITE-007, SITE-011, SITE-012, POST-011
- **Rules affected:** .claude/rules/sites.md, .claude/rules/content.md
- **Detail:** [customization spec, section 13](../requirements/pantrypress-customization-spec.md)

## Context

Every blog needs a homepage and pages such as About, Start here, and Contact. Free-form page builders
produce inaccessible, heavy, non-deterministic pages and a long tail of layout support. A blank page stalls a
new blogger during "Launch my blog" (BIZ-002).

## Decision

PantryPress has no free-form, drag-and-drop page builder: no columns, canvases, absolute positioning, or
per-element styling.

The homepage and site pages share one **section library** of typed, data-aware sections: featured recipe,
latest posts, category row, recipe grid, about strip, newsletter signup, and start-here links.

- The homepage is an ordered list of sections.
- Site pages are block documents that can include those sections as blocks, plus a few page-only blocks:
  intro with photo, FAQ, social links, and contact form.
- New sites get page templates (About, Start here, Contact) as starting content, never as locked layouts.

Every section and block inherits the site's design settings (ADR 0017).

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Shared section library, block pages, templates (chosen) | Less layout freedom. Accessibility and the budget hold on every page. |
| Drag-and-drop page builder | Maximum freedom, but breaks accessibility, the budget, and determinism, and is a large build. |
| Fixed homepage and fixed page layouts | Least work, but every site looks the same and pages like Start here can't be composed. |

## Consequences

- Data-backed sections are part of page data and rebuild after publish (ARCH-014).
- Template placeholders (`[[like this]]`) block publishing until they are replaced (CUS-T15).
- Every section and page block passes overflow and axe checks in every preset (CUS-T14).
- Any prompt that adds columns, free positioning, or per-element styling stops and asks.

## Open items

- **POST-011:** whether FAQ blocks emit `FAQPage` JSON-LD. Default assumed: off, with a per-block opt-in by
  the blogger. Closes at PP8. See the [register](pantrypress.md#3-open-decide-items).

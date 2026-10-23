# 0005 — Structured recipes and deterministic calculations

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D05
- **Requirements:** RCP-001 to RCP-010, RND-003, KPI-005
- **Rules affected:** .claude/rules/recipes.md, .claude/rules/rendering.md

## Context

The recipe card is the core of a food blog. The blogger's ingredient and step wording is their voice, and
readers and search engines both read it. Scaling with floating point produces "0.333 cups". If the browser
did the arithmetic, the page would not work without JavaScript and could differ between renders. Recipe rich
results (KPI-005) need structured fields, not prose. AI is out of bounds (ADR 0012), so parsing and
calculation must be plain code.

## Decision

Recipes are structured data, not rich text. Scaling, unit conversion, temperature conversion, time totals, and
nutrition-per-serving arithmetic are deterministic domain code in PantryPress. Quantities are exact rationals,
including ranges and mixed numbers.

Each ingredient line stores the blogger's original text, plus a parsed quantity, unit, item, and note.
Parsing enriches the original and never rewrites it. Scaled and converted variants are precomputed when page
data is built, and the browser only switches between them.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Structured recipes, exact rationals, precomputed variants (chosen) | A parser and a rational type to build and test, and larger page data. Exact, repeatable output. |
| Rich-text recipes | An easy editor, but no reliable scaling, conversion, or JSON-LD. |
| Floating-point quantities | Standard types, but rounding errors (⅓ × 3 ≠ 1) and unstable fraction display. |
| Scaling in the browser | Smaller page data, but needs JavaScript, breaks no-JS pages (ARCH-008), and moves arithmetic out of tested code. |
| Normalizing ingredient text on parse | Cleaner data, but rewrites the blogger's wording. |

## Consequences

- Unparseable lines are kept and flagged, never dropped. Scaled lines replace only the quantity span.
- Conversions come from platform reference data. Volume converts to weight only when a density is known.
- Nutrition is entered by the blogger. PantryPress never estimates it.
- Golden tests cover parser corpora, scaling tables, conversions, and JSON-LD. Property tests prove that
  scaling by k and then 1/k returns the original quantity.
- One JSON-LD generator, identical across recipe card styles, with a missing-fields report (RCP-008).

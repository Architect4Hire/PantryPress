---
name: add-recipe-feature
description: Change recipe models, parsing, scaling, conversion, nutrition, or JSON-LD while preserving the blogger's wording and keeping calculations deterministic.
---

# Add a recipe feature

Read first: `.claude/rules/recipes.md`, `content.md`.

## Steps

1. Identify which of RCP-001 to RCP-010 the change serves.
2. Change value objects or business code in `Modules/Recipes`. Quantities stay exact rationals.
3. If parsing changes, extend the golden corpus first with real-world lines (fractions, ranges, unicode
   fractions, "to taste", notes in parentheses) and show the before and after.
4. If output changes, update page-data mapping so variants are precomputed, and the JSON-LD generator.
5. Update the recipe editor if the field is editable.
6. Map the field in the publishing API format and the WordPress importer if relevant.

## Checklist

- [ ] Original wording round-trips unchanged.
- [ ] Golden tests for parser, scaling, conversion, and JSON-LD.
- [ ] Property test for scaling round trips.
- [ ] Rich Results validation still passes.

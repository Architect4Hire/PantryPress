# Recipes

Requirements RCP-001 to RCP-010, PP-D05. Use `add-recipe-feature`.

## Wording is sacred

- Each ingredient line stores the blogger's original text plus parsed quantity, unit, item, and note.
- Parsing enriches; it never rewrites the original. Unparseable lines are kept and flagged.
- Rendering shows the blogger's wording. Scaled lines replace only the quantity span.

## Deterministic calculations

- Quantities are exact rationals (numerator, denominator), including ranges ("2–3") and mixed numbers.
  **No floating point** for quantities.
- Scaling: ½×, 1×, 2×, 3×, and custom servings, with documented rounding and fraction display (⅛, ¼, ⅓, ½,
  ⅔, ¾). Unscalable lines show unscaled and flagged.
- Conversion: US and metric for volume, weight, and temperature from platform reference data. Volume to
  weight only when a density is known.
- Time totals and nutrition per serving are arithmetic only. Nutrition is creator-entered; PantryPress never
  estimates it, and diet labels appear only when the blogger sets them (RCP-006).
- Every scaled and converted variant is precomputed when page data is built (RND-003). The browser only
  switches between them.

## Model

- Recipes are structured, not rich text. A post's recipe card block references a recipe (RCP-009).
- Published recipe revisions are immutable; editing creates a draft (RCP-007).
- Badges come from a closed list, max 4; the chef note is plain text, max 280 characters (RCP-010).

## JSON-LD

- One deterministic generator for schema.org `Recipe` with a missing-required-fields report (RCP-008).
- `AggregateRating` only from real ratings. JSON-LD is identical across recipe card styles.

## Tests

- Golden tests for parser input corpora, scaling tables, conversions, and JSON-LD.
- Property tests: scaling by k then 1/k returns the original quantity.

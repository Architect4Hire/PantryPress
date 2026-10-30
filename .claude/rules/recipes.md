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

## Reference vocabulary (PP4.1)

- Units (each with a system, a dimension, and its spellings), courses, cuisines, diets, and allergens are platform
  reference data (PP-D06): no `SiteId`, read-only to the app (`IReferenceData`), seeded from the versioned lists in
  `Modules/Recipes/Business/` (`Units`, `Courses`, `Cuisines`, `Diets`, `Allergens`). A change is a list edit, a version
  bump, and a reviewed migration.
- A recipe stores a word's kebab-case code, never its name. Codes never change and entries are never removed; a name may
  be corrected.
- A unit alias is an exact spelling. A case-insensitive one is stored lower-case; only a case-sensitive pair may differ by
  case (`T` is a tablespoon, `t` a teaspoon). No spelling names two units, and there is no bare `C` or `F` temperature.
- Volume is US customary or metric; imperial measures are not listed. Conversion factors and densities are PP4.8's.
- A diet maps to a schema.org `RestrictedDiet` member where one exists; diets and allergens appear only as the blogger
  sets them, never inferred (RCP-006). The allergens are the US FDA's nine and the EU's fourteen together.
- Only the Recipes module and the migrations name the vocabulary's tables (`DomainBoundaryTests`).

## JSON-LD

- One deterministic generator for schema.org `Recipe` with a missing-required-fields report (RCP-008).
- `AggregateRating` only from real ratings. JSON-LD is identical across recipe card styles.

## Tests

- Golden tests for parser input corpora, scaling tables, conversions, and JSON-LD.
- Property tests: scaling by k then 1/k returns the original quantity.

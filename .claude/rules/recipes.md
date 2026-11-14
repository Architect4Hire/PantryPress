# Recipes

Requirements RCP-001 to RCP-010, PP-D05. Use `add-recipe-feature`.

**The detail is `docs/requirements/pantrypress-recipes-spec.md`.** Read its section before changing that area; a
prompt's `recipes.md, "<Section>"` means that spec section. This file keeps only what always binds.

## Invariants

- **Wording is sacred (RCP-002).** An ingredient line stores `OriginalText` exactly as written, never trimmed, beside its
  parsed parts and UTF-16 spans. Parsing enriches, never rewrites; an unreadable line is kept `Unparsed`, never guessed.
  Scaling and conversion write anew only the quantity span, the unit span, and temperatures; everything else is the
  blogger's. Readers see the blogger's words. Text that is not well-formed UTF-16 is refused at input, never replaced.
- **Exact and deterministic.** Quantities are `Rational` (lowest terms, checked 64-bit, throws on overflow) through
  `Quantity`, `QuantityText`, and `StoredQuantity` only. Nothing in Recipes' `Business/` names a floating-point type,
  `Math`, or a floating-point literal (a test scans every file), nor `decimal`, a culture, a clock, a regular expression,
  or a model. Exact until one rounding step.
- **Business is pure.** Parser, paste reader, scaler, converter, times, nutrition, and JSON-LD live in `Business/` and do
  no I/O. Nothing outside Recipes names the quantity types; other modules get text through a Recipes contract.
- **Nothing is invented (RCP-006).** Nutrition, diets, and allergens are only what the blogger entered: never estimated,
  derived, or inferred. Total time is the sum of the times entered, never stored. JSON-LD states only the snapshot's and
  the page's facts and reports what is missing.
- **Precomputed.** Every scaled and converted variant is built with page data (RND-003); the browser only switches.
- **Reference data is read-only.** Units and the course, cuisine, diet, and allergen vocabularies are `IReferenceData`
  tables seeded from versioned lists in `Business/`; a change is a list edit, a version bump, and a reviewed migration.
  Codes never change and entries are never removed. `UnitConversions`, `IngredientDensities`, and `RecipeVideoHosts` are
  code-only lists. None is ever taken from a request.
- **Working copy vs. revisions (PP-D04).** The recipe tables are the site-owned, private working copy, never read by page
  data. A `RecipeRevision` (Draft or Published) is an `IAppendOnly` canonical JSON snapshot with its SHA-256: never
  changed or removed, and a recipe that has one is archived, never deleted. Golden snapshot bytes never move without a
  `SnapshotVersion` bump. Autosave never makes a revision.
- **One lock, one token.** `Recipe.Version` guards the whole recipe. Every writer of a part takes `FindLockedAsync`
  (`UPDLOCK, ROWLOCK`, never `HOLDLOCK`) and updates the recipe row in the same transaction, so a freeze never captures
  half an edit. A stale version is `recipe-changed`, never last-write-wins.
- **Server re-reads.** Saves parse every ingredient line again on the server; a client's parsed parts are never read.
  Paste-to-parse answers are advice only. A client never chooses a part's key.
- **Who changes what (SITE-010).** Owner and Editor change any recipe; an Author only one they made
  (`CreatedByAccountId`), else `recipe-not-yours`. Every member reads, lists, and duplicates.
- **Wire contracts.** Kebab-case names of `IngredientParseProblem`, `StepPasteProblem`, `UnscaledReason`, `Nutrient`, and
  `RecipeJsonLdIssue` are, or become, wire contracts: never renamed.

## Golden files

Parser lines, pastes, scaling, conversion, and JSON-LD each have a golden corpus under
`PantryPress.Tests/Recipes/Corpus/`, approved by reading, with nothing that rewrites them. A behavior change shows its
moved entries and each is edited by hand; never re-approve in bulk. Property test: scaling by k then 1/k returns the
original quantity.

## Spec map

| Area | Spec section |
| --- | --- |
| `Rational`, `Quantity`, `QuantityText` | Quantity (PP4.2) |
| Ingredient parser, grammar, units, `Unparsed` reasons | Parsing (PP4.5) |
| Paste routes and `PastedListReader` | Paste-to-parse (PP4.6) |
| `RecipeScaler`, factors, `FriendlyRounding`, wording | Scaling (PP4.7) |
| `RecipeConverter`, densities, temperatures | Conversion (PP4.8) |
| `RecipeTimes`, `NutritionFacts` | Times and nutrition (PP4.9) |
| Tables, parts, stored quantity | Model |
| `RecipeRevision`, snapshot, `CreateRevisionAsync` | Revisions (PP4.4) |
| Routes, save rules, list, search, limits, rate limits | Recipe routes (PP4.11) |
| Units and vocabularies | Reference vocabulary (PP4.1) |
| `RecipeJsonLd` | JSON-LD (PP4.10) |

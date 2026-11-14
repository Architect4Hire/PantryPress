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
  weight only when a density is known ("Conversion (PP4.8)").
- Time totals and nutrition per serving are arithmetic only. Nutrition is creator-entered; PantryPress never
  estimates it, and diet labels appear only when the blogger sets them (RCP-006).
- Every scaled and converted variant is precomputed when page data is built (RND-003). The browser only
  switches between them.

## Quantity (PP4.2)

- `Rational`, `Quantity`, and `QuantityText` in `Modules/Recipes/Business/` are the one way a quantity is held and written.
  Nothing outside Recipes names them; another module gets formatted text through a Recipes contract. The stored enums,
  `QuantityKind` and `QuantityStyle`, are in `Contracts/`, so a contract can say which a line is.
- `Rational` is always in lowest terms with a positive denominator, worked in 128 bits and checked back into 64: a result
  that cannot fit throws, never wraps or rounds. Nothing in Recipes' `Business/` names a floating-point type, `Math`, or a
  floating-point literal; a test scans every file there.
- `Quantity` is an `Amount` above zero, a `Range` whose low end is above zero and below its high end, or `ToTaste`, which
  has no amount (`HasAmount`) and is never scaled. `MultiplyBy` is exact. Rounding, and whether a whole line scales, are
  the scaler's ("Scaling (PP4.7)" below).
- `QuantityText` never rounds, reads no culture, and writes ASCII digits and `.`, exactly for every value a `Rational`
  holds. Fraction style is a mixed number: halves, thirds, quarters, and eighths are one character joined to the whole
  (`1½`), and any other remainder is ASCII after a space (`1 5/16`). Decimal style writes a value that ends within three
  places (`0.125`), and any other value as a fraction; a range is decimals only if both ends are. A range uses an en dash
  with no spaces (`2–3`). `to taste` labels the structured line (editor, paste-to-parse); readers see the blogger's own
  words for it.

## Parsing (PP4.5)

- `IngredientLineParser.Parse` in `Business/` reads one line into exactly the parts a line stores: status, quantity and
  its span, unit code and its span, item, and note. It is pure and deterministic: no model, no culture, no regular
  expression (`IngredientLineTokenizer` is one linear pass). It never rewrites the line: the spans index it in UTF-16 code
  units, and the item and each note piece are cut from it verbatim, with only spaces, invisible format characters, and
  `, ; : .` left off their edges (a period that starts a decimal stays).
- Invisible format characters a paste brings (a byte-order mark, a zero-width space, a direction mark, a soft hyphen) read
  as whitespace. A code point Unicode has not assigned is refused. Characters are classified by the runtime's Unicode
  tables, so a runtime upgrade could move one; the golden corpus is the tripwire.
- Grammar: `[bullet] [quantity] [size] [unit] ["of"] item [, note | (note)]…`.
  - Quantities: whole numbers, decimals, fractions (`/` or `⁄`), mixed numbers (`1 1/2`, `1-1/2`, `1-½`, `1½`), every
    vulgar fraction, the words `one` to `twelve`, and ranges joined by a dash or by `to` between spaces. `a` and `an` count
    as one, but only when a unit follows.
  - The parse also says how the amount was written (`Style`): `Decimal` with a decimal point, `Fraction` otherwise. It is
    not stored, since reading the line again gives it again. `QuantityStyle` has no member for `1 1/2` against `1½`, so
    PP4.7 reads that difference at the quantity's span, which keeps the blogger's own style when a scaled amount
    replaces it (the PP4.2 and PP4.3 hand-off).
  - A size straight after the quantity, either in parentheses or hyphenated (`14-ounce`), goes to the note. On a line
    with no quantity, `to taste` is its quantity.
  - The item comes before the first comma. The note is every other piece in the order written, joined by `; `.
- Units come from `Units.Aliases` through `UnitSpellings`, which adds only these rules:
  - a case-sensitive spelling (`T`, `t`) matches exactly, and a case-insensitive one folds ASCII letters only, so no
    look-alike (the Kelvin sign) becomes a unit;
  - a space in a spelling matches any whitespace run;
  - the longest spelling wins;
  - a unit ends where a word does.
- A line is `Unparsed`, with an `IngredientParseProblem` reason (never stored), rather than guessed at:
  - two measures together, a fraction split by spaces (`1 / 2`), a comma inside a number, or a number joined to a word
    that is not a unit;
  - a zero, a malformed fraction, or a reversed range;
  - a term over `MaxQuantityTerm`, or non-ASCII digits;
  - unbalanced parentheses, a broken character, or a control character;
  - no item, or a part over its stored length.
- A number later in the item (`Juice of 2 lemons`, `1 cup (120 g)` in the note) is the item's or the note's. The scaler
  flags such a line rather than scale it, and puts a unit word in the right number ("Scaling (PP4.7)" below).
- The golden corpus is `PantryPress.Tests/Recipes/Corpus/ingredient-lines.txt`, approved by reading, with nothing that
  rewrites it. A change to the parser shows its moved lines, and each moved line is edited by hand.

## Paste-to-parse (PP4.6)

- `POST /api/v1/sites/{siteSlug}/recipes/paste/ingredients` and `…/steps` (`SiteAuthor`) read a pasted list into
  proposed groups and lines for review (RCP-003). They save nothing, read no site data, and answer every member of every
  site alike for the same paste. Limits are `RecipeLimits.MaxPastedTextLength` (30,000) and `MaxPastedLines` (200
  non-blank lines), and `RateLimits:RecipePastesPerAccountPerMinute` (30).
- `PastedListReader` in `Business/` is pure, like the parser. Each line is kept exactly as pasted (`originalText`); a
  heading or a step's text is a cut of its line, with only the marks around it left off.
  - Lines split at CR LF, LF, CR, U+0085, U+2028, and U+2029. A blank line (whitespace and format characters only) is
    skipped and never starts a group.
  - A heading, at most `MaxGroupHeadingLength` once cut and holding a letter, is a line ending in `:` that does not start
    with a number, a Markdown heading (one to six `#` and a space), or - in an ingredient list only - a line starting with
    the word `For` with no number, comma, colon, or parenthesis. An ingredient list leaves a leading bullet off a heading;
    in a step list a line with a step marker is always a step. A group also carries its heading's line exactly as pasted
    and the heading's span in it, so a line misread as a heading can be turned back into a line in the blogger's words.
    `For` and `Step` fold ASCII letters only, as unit spellings do.
  - Ingredient lines go through `IngredientLineParser` unchanged; unreadable ones are kept and flagged with their
    `IngredientParseProblem`, whose kebab-case names are now a wire contract.
  - A step is one line with a leading marker cut off: a parser bullet; up to three digits and `.`, `)`, or `:` before a
    space; or `Step N` with optional punctuation. A line that is only a marker, too long, or holding a control character
    is flagged (`StepPasteProblem`), never dropped. A lone surrogate never arrives: the JSON edge refuses it as a
    `validation-failed` on `text`.
- What the routes propose is advice. The routes that save a recipe (PP4.11) read each line again on the server and never
  take a client's parts on trust.
- The golden pastes are `PantryPress.Tests/Recipes/Corpus/paste/`, `<name>.<ingredients|steps>.txt` beside its
  `.approved.txt`, approved by reading, with nothing that rewrites them.

## Scaling (PP4.7)

- `RecipeScaler` in `Business/` is pure and deterministic: exact until one rounding step, no floating point, culture, or
  clock. `ScaleLine` takes a stored line (`ScalableIngredientLine`: the text and its parsed parts) and a `ScaleFactor`;
  `ScaleYield` scales the yield. Steps are never scaled.
- Factors are exact. The fixed choices are ½×, 1×, 2×, and 3× (`ScaleFactor.Multipliers`). Custom servings
  (`ForServings`) are servings over the yield, offered only for a yield of one whole number. Both terms of a factor are at
  most `MaxQuantityTerm`.
- What a line shows, in this order:
  - At 1×, every line is exactly as written.
  - An unparsed line is as written, flagged `unparsed` (`UnscaledReason`).
  - A temperature line is as written, unflagged.
  - A number outside the quantity is as written, flagged `other-amounts` (`1 cup (120 g) flour`, `Juice of 2 lemons`).
    These are not counted: a size between the quantity and the unit or item (`1 (14 oz) can`, `1 9-inch pie crust`), a
    percentage (`2% milk`), and a temperature (`110 °F`, `105-115°F`).
  - A line with no amount (`salt`, "to taste") is as written, unflagged.
  - Otherwise the amount is multiplied and rounded. An amount that rounds back to the blogger's own is as written.
  - The new line is read again with the parser and must give back the new amount at its span. When a size's fraction
    would join it (`a ½-inch piece` doubled reads `2 ½-inch`, which is 2½), the line is as written, flagged
    `other-amounts`. An amount past `MaxQuantityTerm` is display only and is not read back.
- Rounding (`FriendlyRounding`) goes to the nearest friendly amount, a tie to the larger, never to zero. The band is
  chosen by the exact value, and each band's lower edge is an amount of the band below too, so rounding is monotone and
  idempotent.
  - Kitchen (US measures, count units, no unit): under ⅛ sixteenths, under 10 eighths and thirds, under 20 halves, then
    whole numbers. A decimal writer gets no thirds.
  - Metric (g, kg, ml, dl, l): under 1 twentieths, under 10 tenths, under 100 whole numbers, then fives.
  - Whole (pinch, dash, drop, handful): whole numbers, at least one.
  - A range rounds each end; ends that round alike make one amount.
- Wording (RCP-002): only the quantity span is written anew. It keeps the blogger's style: decimals stay decimals, ASCII
  fractions stay ASCII (`QuantityText`'s `asciiFractions`), and anything else is written with fraction characters. Metric is
  always decimal. A range keeps its `to`; a dash becomes an en dash.
  - The unit span changes only to agree in number, and only for a word in `UnitInflections` (`a pinch` doubled reads
    `2 pinches`). The list is spelled-out words only: an abbreviation (`lbs`), the item (`1 eggs`), and the yield's word are
    never changed.
- Page data precomputes every variant (RND-003, PP7.11); PP4.15 previews them. `UnscaledReason`'s kebab-case names become
  a wire contract when page data carries them.
- The golden table is `PantryPress.Tests/Recipes/Corpus/scaling.txt`: real lines at ½×, 2×, 3×, ¾×, and 5⁄4×, approved by
  reading, with nothing that rewrites it.

## Conversion (PP4.8)

- `RecipeConverter` in `Business/` is pure and deterministic, like the scaler. A recipe card's toggle has three settings: as
  written (`RecipeScaler.ScaleLine`), US, and metric. `ConvertLine(line, factor, target)` makes a converted variant at any
  scale, and `ConvertStep(text, target)` converts a step's temperatures. Steps are never scaled.
- The lists are platform reference data (PP-D06) held as code only (owner decision, PP4.8). Each is versioned, has no site,
  and is never taken from a request. They have no table, because nothing stored refers to them.
  - `UnitConversions`: each US and metric volume and weight unit's exact size in ml or g, from its legal definition. A US
    gallon is 3.785411784 L, and a teaspoon is 1/768 of one. A pound is 453.59237 g.
  - `IngredientDensities`: twelve ingredients, in grams per US cup, from King Arthur Baking's chart.
  - A table comes with the first thing that stores a density's code.
- What a line shows, in this order:
  - An unparsed line is as written, flagged `unparsed`, in every converted variant, 1× included.
  - A temperature line in the other system is converted at every scale, since temperatures never scale. One that would
    convert to zero or below stays as written.
  - A line whose measure does not convert is exactly what the scaler shows: no unit, a count, a unit already in the target
    system, or a tsp or tbsp in metric, which metric kitchens use too. Temperatures in the rest of it still convert.
  - A line whose measure would convert is as written, flagged `other-amounts`, when it has another number
    (`1 cup (240 ml) milk`) or a size before its unit.
  - Otherwise the exact scaled amount is converted exactly (`ExactValue`, no size limit) and rounded once by
    `FriendlyRounding`: the metric grid for metric, the kitchen grid for US. This is the PP4.7 hand-off: never round, then
    convert.
  - The new line is read again, and must give back its amount and unit at its span.
- Volume and weight cross only through a density.
  - US volume becomes grams in metric, and grams become US volume in US, never the other way round.
  - The item, folded, must equal one of an entry's spellings exactly. Folding lower-cases ASCII letters, makes each run of
    whitespace one space, and turns a curly apostrophe straight. The note is never read, so `1 cup flour, sifted` is
    all-purpose flour, and `1 cup sifted flour` becomes ml.
- The target unit is chosen by the exact amount (a range's low end), and rounding never changes it:
  - metric: ml below 1000, then L; g below 1000, then kg;
  - US: tsp below a tablespoon, tbsp below a quarter cup, then cups; oz below a pound, then lb.
- Wording (RCP-002): only the quantity span, the unit span, and each temperature's numbers and unit are written anew.
  - Metric is decimal. US is fractions, ASCII when the blogger's were. A range keeps its `to`, and a dash becomes an en dash.
  - A spelled-out unit becomes the target's word, agreeing in number. An abbreviation becomes the target's abbreviation, or
    the word where there is none (cup). A word is never joined to its number (`250g` reads `1 cup`).
- Temperatures (`TemperatureMentions`) are digits with an optional decimal part, an optional range by dash or `to`, then a
  temperature spelling. A number with no scale (`350 degrees`, `350F`) is no temperature.
  - The conversion is exact, then rounded: whole degrees below 100 °C or 215 °F, and fives at or above (`RoundTemperature`),
    a tie to the warmer. Both thresholds are multiples of five, so rounding is monotone; 100 °C still shows 212 °F.
  - The unit keeps its spelling with the scale swapped (`°F` to `°C`, `degrees f` to `degrees c`).
  - A text's temperatures convert together or not at all. One in the target system, one joined to another number
    (`110°F/43°C`) or after a minus or a joined dash, or a range that would start below zero keeps them all as written. A
    single temperature below zero is written with a minus sign (`0°F` is `-18°C`).
- `ScaledIngredientLine.IsConverted` says a variant wrote any of the line in the other system. `IsRounded` is about the
  converted amount.
- The golden table is `PantryPress.Tests/Recipes/Corpus/conversion.txt`. It holds real lines in metric and US at 1× and at a
  second scale, and real steps, approved by reading, with nothing that rewrites it.

## Times and nutrition (PP4.9)

- `RecipeTimes` and `NutritionFacts` in `Business/` are pure, like the scaler: whole minutes and whole hundredths, no floating
  point, `decimal`, culture, or clock. What each time or nutrient is called, and the disclaimer, are the recipe card's (PP7.7).
- Times (RCP-001):
  - prep, cook, and additional are exactly as entered. An empty time was not entered; zero was, so it shows (`0 min`) and
    counts (owner decision).
  - The total is the sum of the times entered, never stored. With none entered there is no total.
  - `RecipeDuration` writes `20 min`, `2 hr 20 min`, `1 day 3 hr`, and `2 days`: days from 24 hours, leaving out each zero
    part. Its ISO 8601 form is hours and minutes only (`PT27H`, `PT0M`), since an ISO day is a calendar day.
- Nutrition (RCP-006, RND-004, CMP-005): only what the blogger entered, per serving, and nothing else.
  - The nutrients entered are listed in `Nutrient`'s order (a US label's); an empty one is left out, and zero is shown.
  - Nothing is derived (no calories from the macronutrients, no unsaturated fat from total fat), rounded, or totalled for the
    recipe. Nothing takes a yield or a scale, so every scaled variant shows the same facts.
  - An amount is its hundredths written exactly: ASCII digits and `.`, no trailing zero after the point, no thousands
    separator (`14`, `14.5`, `2300`; owner decision). Units are `kcal`, `g`, and `mg`, as the columns are named.
  - The serving size is the blogger's own words. A panel shows only when a value was entered (`HasNutrition`), and always
    with the disclaimer.
- Each is made only by its `From`: no public constructor and no setter, so a total is always its times' sum and the facts are
  always what was entered, in order. `NutritionFacts` is equal by value, facts compared in order.
- `Nutrient`'s kebab-case names become a wire contract when page data carries them (PP7.11).

## Model

- Recipes are structured, not rich text. A post's recipe card block references a recipe (RCP-009).
- Published recipe revisions are immutable; editing creates a draft (RCP-007; see "Revisions" below).
- The recipe tables (PP4.3) are the live editing tables: site-owned and private, never read by page data (ARCH-004).
  `Recipe` holds title, summary, yield, prep/cook/additional minutes, video URL, and nutrition per serving. Its parts are
  their own rows, all keyed `(SiteId, …)`:
  - ingredient groups and their lines;
  - step groups and their steps;
  - equipment, notes, and keywords, each a list keyed by position;
  - courses, cuisines, diets, and allergens, each a set of vocabulary codes under a foreign key, with no navigation.
- Each part refers to its parent by `(SiteId, id)`, and a recipe's delete cascades to its parts. There is no foreign key to
  `Sites`.
- An ingredient line keeps `OriginalText` exactly as written, never trimmed. Beside it are the parse status, the quantity,
  the unit code, the item, and the note, plus where the quantity and the unit are in the text, counted in UTF-16 code units.
  - A line that is `Unparsed` has none of these.
  - An amount or a range always has its span, and a unit always has its span.
- A stored quantity's columns are written and read only through `StoredQuantity`: the kind, then numerator and denominator
  for each end.
  - Each term is from 1 to `RecipeLimits.MaxQuantityTerm` (1,000,000).
  - A yield is an amount or a range, never "to taste"; its unit is the blogger's own word.
- What is not stored:
  - Total time is the sum of the other times (PP4.9).
  - Diets and allergens exist only as the blogger's choices.
  - Nutrition is `decimal(9,2)` as entered, never estimated.
- `Recipe.Version` is the one concurrency token, so a change to any part moves the recipe's `UpdatedAt` (PP4.11).
- Badges come from a closed list, max 4; the chef note is plain text, max 280 characters (RCP-010).

## Revisions (PP4.4)

- The recipe tables are the working copy: the draft a blogger edits. A revision (`RecipeRevision`) freezes it at one
  moment. It is site-owned and private, and it is one of two kinds:
  - **Draft**: a save point;
  - **Published**: what page data, JSON-LD, and a post's recipe card read (PP-D04).
- Both kinds are written once and never changed or removed (`IAppendOnly`, `backend.md`). Editing a published recipe
  changes only the working copy, so what readers saw stays as it was. A revision's foreign key to its recipe is `Restrict`:
  a recipe that has one is archived, never deleted.
- A revision holds the whole recipe as one canonical JSON snapshot (`RecipeSnapshotJson`, format `SnapshotVersion`), with
  the SHA-256 of its bytes. Canonical means:
  - every property is written, nulls included, in a fixed order;
  - parts come by position, then by id (or by code, for a vocabulary choice);
  - every character outside ASCII is escaped;
  - numbers are whole only, and nutrition is held in hundredths.
- The snapshot keeps the blogger's text exactly. Text that is not well-formed UTF-16 is refused, never replaced. A revision
  is never rewritten, so every snapshot format ever written stays readable.
  - If the golden bytes ever move (a runtime escaping differently), the format version moves with them. They are never
    simply re-approved.
  - SqlClient reads a lone surrogate stored in a column back as U+FFFD (found at PP4.4). So keeping the words exactly is
    the input's job: the recipe routes, import, and the publishing API refuse ill-formed text before it is stored.
- `IRecipeRevisionsFacade.CreateRevisionAsync` freezes the working copy as a given kind, given the recipe's version as the
  caller read it. The facade decides everything inside `RecipesUnitOfWork.RunLockedAsync`, one transaction that runs
  again on each retry. Its first read takes an update lock on the recipe's row (`UPDLOCK, ROWLOCK`, never `HOLDLOCK`: on a
  miss, a range lock could reach another site's row). Then:
  - an unknown or other site's recipe is `recipe-not-found`;
  - a revision with the call's own id is the answer, for a retry whose commit landed;
  - a stale version is `recipe-changed`;
  - the same bytes as the latest revision of that kind return that revision with `Created: false`, and nothing is added;
  - otherwise the revision is staged and saved under the next number, counting both kinds.
- The lock, with read-committed snapshot, is what keeps a freeze from ever capturing half of an edit. It holds only if every
  write to a part also updates the recipe's row in the same transaction. PP4.11 makes its edits take `FindLockedAsync`
  first, and so does any other writer of a part (re-parse, import, the publishing API, step images).
- Autosave writes only the working copy. A draft revision is a save point someone asked for, never an autosave, because
  revisions are never pruned.
- Erasing a site or an account (ADR 0003, PP19.11a) lists `RecipeRevisions` itself and changes its trigger on purpose.

## Reference vocabulary (PP4.1)

- Units (each with a system, a dimension, and its spellings), courses, cuisines, diets, and allergens are platform
  reference data (PP-D06): no `SiteId`, read-only to the app (`IReferenceData`), seeded from the versioned lists in
  `Modules/Recipes/Business/` (`Units`, `Courses`, `Cuisines`, `Diets`, `Allergens`). A change is a list edit, a version
  bump, and a reviewed migration.
- A recipe stores a word's kebab-case code, never its name. Codes never change and entries are never removed; a name may
  be corrected.
- A unit alias is an exact spelling. A case-insensitive one is stored lower-case; only a case-sensitive pair may differ by
  case (`T` is a tablespoon, `t` a teaspoon). No spelling names two units, and there is no bare `C` or `F` temperature.
- Volume is US customary or metric; imperial measures are not listed. Conversion factors and densities are code-only
  lists, `UnitConversions` and `IngredientDensities` ("Conversion (PP4.8)").
- A diet maps to a schema.org `RestrictedDiet` member where one exists; diets and allergens appear only as the blogger
  sets them, never inferred (RCP-006). The allergens are the US FDA's nine and the EU's fourteen together.
- Only the Recipes module and the migrations name the vocabulary's tables (`DomainBoundaryTests`).

## JSON-LD (PP4.10)

- `RecipeJsonLd.Write(snapshot, context)` in `Business/` is the one generator of schema.org `Recipe` JSON-LD (RCP-008), from
  a published revision's snapshot. It is pure and deterministic, like the scaler, and the recipe card's style never changes
  it (THM-009). It answers the JSON and a missing-fields report (`RecipeJsonLdIssue`, kebab-case, pinned).
  - Each issue is `Required` (no rich result without it: `image-missing`, `yield-missing-for-nutrition`) or `Recommended`.
  - The report names facts, never fixes. The preview (PP4.15) and the readiness check (POST-006) word them.
- What a revision does not hold is the page's: its address, images, author, and publish date (`RecipeJsonLdContext`), from
  the post, its media, and the site (PP7.11). None is guessed. One left out is reported. Addresses are absolute HTTPS and
  the date is UTC, written to the second.
- Every fact is the blogger's (RCP-002, RCP-006):
  - name, summary, ingredient lines (`OriginalText`, no group headings), and steps exactly as stored;
  - times as entered, zero included (`PT0M`; owner decision), and their sum as `totalTime`. A missing prep or cook time is
    reported by name;
  - the yield as the card writes it at 1× (`8 servings`, `2–3 loaves`);
  - courses and cuisines as their names, and diets as their `RestrictedDiet` member, left out where there is none;
  - nutrition only as entered, as schema.org's text (`270 calories`, `14.5 grams`, `75 milligrams`), never the card's
    `kcal`. A serving size alone is no nutrition. Nutrition is written only beside a yield, because Google requires one
    with it and would otherwise refuse the whole recipe; until then it is withheld and `yield-missing-for-nutrition` asks.
  - A blank title is a bug (the table refuses one) and throws. Steps and lines are written as stored; the tables refuse
    blank ones.
- A keyword that repeats the recipe's own course or cuisine is left out, compared with ASCII letters folded and its ends'
  white space ignored; the rest stay as written (owner decision).
- Never written:
  - a rating or review: `aggregateRating` belongs inside the Recipe, and PP12.8 adds it here from approved ratings only,
    through a typed input on the context (RND-005);
  - a video, since an address alone cannot meet Google's title, thumbnail, and upload date; it is reported
    (`video-not-described`; owner decision);
  - allergens, equipment, notes, and a step's `url` or `name`.
- Every character outside ASCII and every HTML-sensitive one is escaped by a named `JavaScriptEncoder.Default`, never a
  relaxed one, so the output is ASCII and safe inside a `<script type="application/ld+json">` element. Context addresses are
  lower-case `https://`, RFC 3986 characters only, with no user name or password.
- Checked against schema.org V30.1 and Google's recipe and video guidance on 2026-11-14. A vocabulary change is a code
  change with its golden files.
- The golden files are `PantryPress.Tests/Recipes/Corpus/json-ld/<case>.approved.txt`, from `RecipeJsonLdCases`. Each is
  the report and then the JSON-LD indented to be read; the compact bytes are the same with the layout removed. They are
  approved by reading, with nothing that rewrites them.

## Tests

- Golden tests for parser input corpora, scaling tables, conversions, and JSON-LD.
- Property tests: scaling by k then 1/k returns the original quantity.

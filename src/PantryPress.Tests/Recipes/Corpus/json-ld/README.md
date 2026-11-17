# Recipe JSON-LD golden corpus (PP4.10, RCP-008)

Each case is a published revision's snapshot and the page it sits on, and what the generator writes for it.

- `<case>.case.json` is the input: `page` (the page's `url`, `imageUrls`, `authorName`, and `datePublished`, or `null` for
  a page that gives nothing, the recipe editor's case) and `snapshot`, a stored snapshot in format 1, read through
  `RecipeSnapshotJson.Read` exactly as a revision's is. Only an ingredient line's `originalText` reaches JSON-LD, so most
  lines are stored unparsed; `complete` keeps parsed ones to show the parts change nothing.
- `<case>.approved.txt` is what the generator writes: the gaps it reports, required then recommended, as their kebab-case
  names (`none` when there are none), then `---`, then the JSON-LD written indented with non-ASCII shown as itself, so it
  can be read. A character outside the Basic Multilingual Plane (an emoji) stays escaped there, as the readable encoder
  writes it.
- `<case>.approved.jsonld`, for `complete` and `unparsed-and-hostile` only, is the production output byte for byte:
  compact, with every character outside ASCII and every HTML-sensitive one (`<`, `>`, `&`, `'`, `"`, `+`) escaped.

Every expectation was written by hand from `recipes.md` ("JSON-LD (PP4.10)") and approved by reading it. Nothing rewrites
these files: when the generator's answer for a case changes on purpose, that file is edited, and the change says why
(CLAUDE.md: golden files are never re-approved in bulk). Each output can be pasted into Google's Rich Results Test or the
Schema Markup Validator; doing that in CI is RND-005's (PP7.10).

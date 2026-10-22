---
name: add-render-artifact
description: Add or change a derived render artifact - page data shapes, home and archive pages, section data, sitemaps, feeds, search index - built by jobs from published revisions.
---

# Add a render artifact

Read first: `.claude/rules/rendering.md`, `content.md`. Template P3.

## Steps

1. Define the versioned page-data shape for the route or artifact.
2. Build it in the Worker only from published revisions and site settings (ARCH-004).
3. Write under the versioned blob prefix; include it in the atomic pointer switch.
4. Decide what triggers a rebuild and make rebuilds incremental where possible (ARCH-014).
5. Add the purge tags the artifact needs.
6. Serve it through `PantryPress.Site` and render it with `add-theme-component`.

## Checklist

- [ ] Deterministic output: golden test for the JSON and the HTML.
- [ ] Rebuild test after publish, unpublish, and slug change.
- [ ] Two-site test for the build and the read path.

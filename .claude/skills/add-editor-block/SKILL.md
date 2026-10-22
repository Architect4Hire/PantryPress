---
name: add-editor-block
description: Add a block to the post or site-page block schema end to end - schema, editor UI, sanitizer, page data, SSR component, import, and publishing-API mapping.
---

# Add an editor block

Read first: `.claude/rules/content.md`, `rendering.md`, `design-system.md`. Template E.

## Steps

1. Define the block in the versioned schema: attributes, limits, allowed marks, where it is allowed (posts,
   site pages, or both). Section and page blocks are site-page only.
2. Editor: node view, toolbar entry, keyboard access, settings panel with typed options only.
3. Sanitizer: allow-list rules for the block's HTML.
4. Page-data mapping and the SSR component in `@pantrypress/ui/themes` (via `add-theme-component`).
5. Readiness checks the block needs (alt text, placeholders).
6. Publishing API format and WordPress import mapping, or an explicit "not supported" note.

## Checklist

- [ ] Schema migration for existing documents if needed.
- [ ] Editor component and axe tests.
- [ ] XSS corpus passes through the sanitizer.
- [ ] Golden files for the rendered block in each preset.
- [ ] No columns, free positioning, or per-block styling (PP-D18).

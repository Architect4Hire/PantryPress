# 0004 — Publishing is a snapshot

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D04
- **Requirements:** ARCH-004, POST-004 to POST-008, RCP-007
- **Rules affected:** .claude/rules/content.md, .claude/rules/rendering.md, .claude/rules/recipes.md

## Context

Bloggers keep editing posts after they publish them, and autosave writes constantly. Readers should see
nothing until the blogger says "Publish". Deterministic rendering (ARCH-005), rollback, and revision compare
all need a stable input that does not change underneath them.

## Decision

Publishing a post creates an immutable **published revision**. It captures the post's blocks, the referenced
recipes' published revisions, media references, SEO fields, and taxonomy at that moment. Page data is built
only from published revisions and site settings. Editing a draft changes nothing public until the blogger
publishes again.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Immutable published revisions (chosen) | More storage, and published recipe revisions must be pinned. Readers never see half-finished edits. |
| A "published" flag on the live row | No copies, but autosave would leak edits to readers immediately, and there is no rollback. |
| Render from drafts with a live/preview toggle | Simpler storage, but page data could include unreviewed text. Breaks ARCH-004. |

## Consequences

- Drafts never reach page data.
- Editing a published recipe creates a new draft revision (RCP-007).
- Revisions support compare and restore; scheduling publishes a revision at a set time.
- Unpublish removes the post's page data. Slug changes create 301 redirects (POST-007).
- The readiness check runs before a revision becomes published (POST-006).

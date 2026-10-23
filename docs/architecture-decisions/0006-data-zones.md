# 0006 — Data zones

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D06
- **Requirements:** PSEC-005, BIZ-006, CMP-003, ARCH-015
- **Rules affected:** .claude/rules/tenancy.md, .claude/rules/backend.md, .claude/rules/public-endpoints.md

## Context

PantryPress holds data with different owners, trust levels, and lifetimes. Reference data such as units is
shared by every site. Blogger content is private until published, and must come down again on unpublish once
it is public. Reader submissions are personal data from anonymous, untrusted sources. Treating all of these
alike would either over-restrict shared data or under-protect personal data.

## Decision

Every piece of data belongs to exactly one zone:

| Zone | `SiteId` | Contents | Handling |
| --- | --- | --- | --- |
| Platform reference data | No | Units, conversions, ingredient densities, diet and allergen vocabulary, cuisines, courses | Versioned; read by every site |
| Site-owned data | Yes, private | Posts, recipes, revisions, media, settings, pages, members | Global query filter; admin access only |
| Published public data | Yes, deliberately public | Page data of published revisions, public media derivatives | Built by jobs; removable on unpublish |
| Reader-submitted data | Yes, personal and untrusted | Comments, ratings, subscribers, contact messages, consent records | Validated, minimized, encrypted at rest, HTML-encoded on output |

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Four explicit zones (chosen) | Every entity must be classified. Clear handling rules per zone. |
| Two zones (platform and site) | Simpler, but reader personal data would get the same handling as blogger content. |
| Platform-wide reader data | Enables cross-site reader features, but those are out of scope (no reader accounts) and it widens the isolation surface. |

## Consequences

- New entities state their zone. Site-owned, public, and reader zones follow ADR 0003.
- Reader data is minimized and encrypted at rest (PSEC-005), and is never logged.
- Unpublish removes public data, and published data never includes drafts (ADR 0004).
- Export (BIZ-006) covers site-owned and reader data for the site's own blogger.

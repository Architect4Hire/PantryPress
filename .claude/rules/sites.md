# Sites, settings, design, and pages

Requirements SITE-001 to SITE-012, THM-001 to THM-013, DOM-001 to DOM-005, PP-D17, PP-D18.
Detail: `docs/requirements/pantrypress-customization-spec.md`.

## Sites

- Slugs: 3–40 lowercase letters, digits, hyphens; unique; reserved list; availability never reveals the
  holder. Renames are limited and the old host redirects for 12 months.
- `SiteHost` maps every host to one site with one canonical host. An active custom domain becomes canonical;
  the subdomain 301s to it and always keeps working.
- Status: Provisioning, Live, Suspended, ReadOnly, Offline, Deleted, with correct public status codes.

## Design settings (PP-D17)

- One versioned `SiteThemeSettings` document per site; typed options only. No custom CSS, HTML, templates,
  scripts, or font uploads.
- Apply builds an immutable theme snapshot (tokens, `data-*` attributes, fonts, sanitized text, sections,
  contrast report). Keep 20 snapshots; revert restores one exactly.
- Colors: the server derives link, fill, and ink colors with WCAG math and refuses unfixable colors (THM-005).
- Presets are values over one component tree, never forks.
- Gated options go through the entitlements facade; downgrade keeps settings and renders defaults (THM-012).

## Pages and sections (PP-D18)

- The homepage is an ordered list of sections from the shared section library. Site pages are block
  documents that can embed sections and page blocks.
- Templates (About, Start here, Contact, legal pages) are starting content, never locked layouts.
- Data-backed sections are part of page data, rebuilt after publish.

## Settings changes

- Settings and design changes purge the site's edge cache through the outbox.
- Preview uses a signed, expiring, site-bound token; previews are no-store and noindex.

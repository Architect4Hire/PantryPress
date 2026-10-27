# Sites, settings, design, and pages

Requirements SITE-001 to SITE-012, THM-001 to THM-013, DOM-001 to DOM-005, PP-D17, PP-D18.
Detail: `docs/requirements/pantrypress-customization-spec.md`.

## Sites

- Slugs: 3–40 lowercase letters, digits, hyphens; unique; reserved list; availability never reveals the
  holder. Renames are limited and the old host redirects for 12 months.
- The slug policy (PP2.8) is `SlugPolicy` in Sites' Business: ASCII checked before lower-casing, so comparison is
  case-insensitive and no look-alike letter becomes a real one; a host name's label (no hyphen at either end, none
  doubled, which also rules out `xn--` look-alikes); not a site id's shape; then the reserved list. A refused shape is
  a `slug` field error; a reserved slug is `slug-unavailable`, the same answer a held one gets, so neither the reason
  nor the holder shows.
- The reserved list is platform reference data: `ReservedSlugs` in Sites' Business, versioned, and seeded into the
  `ReservedSlugs` table, so a change is a reviewed migration. It reserves every host PantryPress answers on or holds
  DNS records under (`app` above all, whose `__Host-` cookies a tenant site there would share; a test reads the
  gateway's hosts and the Terraform). The database refuses a reserved slug on any site write, and refuses reserving a
  slug a site holds, so taking an address from a site is decided on purpose, never by a list edit.
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

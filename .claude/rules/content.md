# Content: blocks, sanitization, publishing

Requirements POST-001 to POST-011, SITE-007, SITE-011, PP-D04, PP-D18.

## Block document

- Posts and site pages are versioned block documents with an allow-listed block and mark schema
  (default editor: TipTap/ProseMirror through an Angular wrapper).
- Post blocks: paragraph (bold, italic, link), heading, image, gallery, list, quote, tip and note callouts,
  allow-listed video embed, recipe card (reference), divider.
- Site-page extras: section blocks from the section library and page blocks (intro, FAQ, social links,
  contact form). No recipe card on site pages.
- No page builder: no columns, free positioning, or per-block colors and fonts (PP-D18).
- New blocks use `add-editor-block` and need: schema, editor UI, sanitizer rules, page-data mapping, SSR
  component, import mapping, and publishing-API mapping.

## Sanitization

- All HTML from the editor, imports, and the publishing API passes the allow-list sanitizer when page data
  is built (RND-002). No inline handlers, `javascript:` URLs, iframes outside allow-listed video hosts, or
  raw HTML blocks.
- Affiliate links get `rel="sponsored nofollow"` automatically (MON-003).

## Publishing

- Publishing creates an immutable published revision: blocks, referenced recipe revisions, media refs, SEO
  fields, taxonomy (PP-D04). Drafts never reach page data.
- Autosave, revisions with compare and restore, schedule in the site time zone, unpublish, republish.
- Slug changes create 301s automatically (POST-007).
- The readiness check lists blocking and warning items in plain language (POST-006), including template
  placeholders (`[[like this]]`) that block publishing.

---
name: add-public-endpoint
description: Add an anonymous endpoint in PantryPress.Site that resolves the site from Host only, with validation, spam defense, rate limits, and host-header isolation tests.
---

# Add a public endpoint

Read first: `.claude/rules/public-endpoints.md`, `tenancy.md`, `rendering.md`.

## Steps

1. Route under `/api/public/v1/…`. No site id in the route, body, or query.
2. Resolve the site from `IPublicSiteContext` (Host → `SiteHost`). Unknown host returns the neutral 404.
3. Reads: serve page data from Redis, then blob, with the in-process LRU. Never SQL.
4. Writes: validate and size-limit; add honeypot, time-to-submit, link limits, and the spam challenge when
   the endpoint takes free text; store minimized data; go through a facade.
5. Apply rate-limit policies per IP, per site, global.
6. Set cache headers: cacheable reads, `no-store` writes.

## Checklist

- [ ] Two-site test with host-header tricks (other host, X-Forwarded-Host, case, trailing dot, custom
      domain).
- [ ] Abuse tests: oversized body, missing challenge, too fast, too many links.
- [ ] No SQL reachable on read paths (test or analyzer).
- [ ] `public-site-reviewer` run.

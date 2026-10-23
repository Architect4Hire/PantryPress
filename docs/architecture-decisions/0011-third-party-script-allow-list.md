# 0011 — Third-party scripts are an allow-list

- **Status:** Accepted, with one open item
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D11
- **Requirements:** MON-001 to MON-005, ANL-004, ENG-003, CMP-004, BIZ-007
- **Rules affected:** .claude/rules/external.md, .claude/rules/rendering.md

## Context

Prosumer bloggers earn through ads (BIZ-007), and some want GA4. Ad scripts are third-party code running on
tenant subdomains next to other tenants. A free-form "add your scripts" field would undo the closed feature
set and the WCAG and performance guarantees, and it would make a meaningful Content Security Policy
impossible.

## Decision

Only AdSense, named ad networks, GA4, Pinterest verification, and the spam challenge can add script to a page,
each through a typed setting. The Content Security Policy is generated per site from its enabled
integrations. An integration adds its origins only when it is turned on.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Typed allow-list with a per-site CSP (chosen) | Each new network is a code change and a review. A tight CSP and known behavior. |
| A free-form header-scripts field | WordPress-style flexibility, but invites XSS and tracking, breaks the CSP and budgets, and violates the closed set. |
| No third-party script at all | Simplest and fastest, but no ad revenue and no GA4. |

## Consequences

- Nobody widens the CSP by hand. Each integration declares its origins in code.
- A consent banner gates non-essential scripts, and GA4 loads only after consent (MON-004, ANL-004).
- Ads are excluded from the RND-007 budget but never render inside the recipe card.
- `integration-reviewer` runs after any integration change.

## Open items

- **D-11:** which ad networks join the list. Default assumed: AdSense plus one network chosen at PP15.5. See
  the [register](pantrypress.md#3-open-decide-items).

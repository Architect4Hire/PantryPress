# 0010 — Trial instead of a free-forever tier

- **Status:** Proposed (default assumed; open as D-10)
- **Date:** 2026-10-23
- **Decided by:** Not yet decided. Recorded at PP0.1 with its default.
- **Decision ID:** PP-D10
- **Requirements:** requirements section 2.3, BIZ-009, KPI-002, KPI-006
- **Rules affected:** .claude/rules/billing.md

## Context

A free subdomain tier is the main abuse and cost risk for a team of one. Free sites attract spam, phishing,
and malware hosting (BIZ-009), and they add storage, rendering, and support load without revenue (KPI-006).
A free tier would also help growth and give the platform an SEO footprint.

## Proposed decision

New accounts start a **14-day trial** with paid-plan features, then pay. There is no free-forever tier.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| 14-day trial, then paid (default) | Lower sign-up volume. Abuse and cost stay bounded. |
| Free Starter forever | Growth and reach, but the largest abuse and cost exposure, and more operator time (KPI-007). |
| Free tier with tight limits and manual review | A middle path, but every new site lands in a review queue for one operator. |

## Consequences if accepted

- Reminders go out before and on the last day of the trial.
- An expired trial shows a "coming soon" page, with content retained.
- Abuse controls (ACCT-003, Safe Browsing, takedown) still ship before public sign-up opens.

## Open items

- **D-10:** free-forever Starter or a trial. Default assumed: 14-day trial. Closes at PP14. See the
  [register](pantrypress.md#3-open-decide-items).

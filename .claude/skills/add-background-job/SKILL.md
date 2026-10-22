---
name: add-background-job
description: Add a Worker job (page-data build, purge, feed, import, webhook processing) that is idempotent, site-scoped, resumable, and observable.
---

# Add a background job

Read first: `.claude/rules/backend.md`, `tenancy.md`, `rendering.md`. Template G.

## Steps

1. Trigger from the outbox or a schedule, never directly from a request.
2. Payload carries `SiteId` and an idempotency key; set the site context before data access.
3. Make each step idempotent and resumable; record progress for long jobs (imports, rebuilds).
4. Throttle platform-wide work (theme release purges) so publishes are not starved (PNFR-005).
5. Emit metrics and structured logs; failed items go to a retry and poison queue with a plain report.

## Checklist

- [ ] Tests for retry, duplicate delivery, and partial failure.
- [ ] Two-site test if the job touches site data.
- [ ] Dashboard or log query documented for operators.

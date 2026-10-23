# 0009 — Subscriptions via Stripe, entitlements in-house

- **Status:** Accepted, with one open item
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D09
- **Requirements:** BILL-001 to BILL-008, ARCH-010, ARCH-011, BIZ-004
- **Rules affected:** .claude/rules/billing.md, .claude/rules/external.md

## Context

Revenue comes from subscription tiers. One developer cannot own invoices, sales tax, card handling, a
customer portal, and dunning. Gated features, however, are checked on many requests and must not depend on a
live call to a payment provider. Downgrades and failed payments must never lose a blogger's content.

## Decision

Stripe owns prices, checkout, invoices, tax, the customer portal, and dunning. PantryPress owns an
**entitlements read model** derived from verified, de-duplicated Stripe webhooks processed through the
outbox. Every gated feature asks the entitlements facade. The subscription belongs to the **account** that
pays, and its entitlements apply to that account's sites.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Stripe billing plus an in-house entitlements read model (chosen) | Webhook processing and reconciliation to build. Fast, local entitlement checks. |
| Query Stripe on every check | No read model, but adds latency and provider rate limits, and a Stripe outage becomes a feature outage. |
| Build billing in-house | Full control, but tax, invoicing, and card-data scope are unrealistic for one person. |
| A merchant-of-record provider | Takes on tax liability, but less control of checkout and the portal. The stack already names Stripe. |

## Consequences

- No code checks a plan name. Limits live in configuration, not code.
- Webhooks are signature-verified, stored raw, de-duplicated by event id, and processed idempotently.
- Stripe ids live only in billing integration records, never on `Account`.
- A downgrade hides or freezes over-limit items with a plain explanation and never deletes them. Failed
  payment moves the site to `ReadOnly`, then `Offline`, with content retained.
- Local runs and tests use a Stripe fake, test mode, and recorded webhook fixtures.

## Open items

- **D-09:** tier prices and limits. Default assumed: the section 2.3 limits, prices to be set. Closes at PP14.
  See the [register](pantrypress.md#3-open-decide-items).

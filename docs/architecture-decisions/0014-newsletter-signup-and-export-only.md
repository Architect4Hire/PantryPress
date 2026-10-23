# 0014 — Newsletter signup and CSV export only

- **Status:** Accepted
- **Date:** 2026-10-22
- **Decided by:** Owner
- **Decision ID:** PP-D14
- **Requirements:** ENG-005, ENG-006, CMP-003; requirements section 1.2 (Newsletters)
- **Rules affected:** CLAUDE.md, .claude/rules/public-endpoints.md, .claude/rules/external.md

## Context

Bloggers want to grow a subscriber list. Sending newsletters means deliverability work, bounce and complaint
handling, anti-spam law compliance, and the risk of one blogger damaging the shared sending domain's
reputation for everyone. Syncing to email services means one integration per provider, stored credentials,
and keeping lists consistent. For one operator, either one is a product of its own.

## Decision

PantryPress collects subscribers with double opt-in, stores consent, and exports the list as CSV. There is no
composing, no campaigns, no ESP setup, no sync, and no sending to subscribers. Platform email is
transactional only: signup confirmations, contact form messages, comment notices, and account and billing
mail.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Signup and CSV export (chosen) | Bloggers bring their own email tool. Small scope, no deliverability burden. |
| Built-in newsletter sending | All-in-one, but deliverability, complaints, and abuse land on the platform's sending domain. |
| Sync to email service providers | Convenient, but per-provider integrations, credentials, and sync correctness to own. |

## Consequences

- Signup endpoints never reveal whether an email is already subscribed.
- The CSV includes consent data. The subscriber cap is an entitlement (ADR 0009).
- Transactional email goes through Azure Communication Services behind a gateway.
- Any prompt that starts sending to subscribers stops and asks.

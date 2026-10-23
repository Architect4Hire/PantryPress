# 0015 — The publishing API is how other tools publish into PantryPress

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D15
- **Requirements:** API-001 to API-009, ARCH-009, BIZ-010, PSEC-008
- **Rules affected:** .claude/rules/publishing-api.md, .claude/rules/auth.md, .claude/rules/api-contract.md,
  .claude/rules/media.md

## Context

CreatorPantry users want to publish what they develop there into their PantryPress blog. PantryPress must not
depend on CreatorPantry (ADR 0001), and BIZ-010 forbids any shared account beyond a per-site key. Other
trusted authoring tools may connect the same way. A public developer platform or app marketplace is out of
scope.

## Decision

PantryPress exposes a versioned REST API at `/api/publishing/v1` on `PantryPress.ApiService`. It is
authenticated with **per-site publishing keys** that a site Owner creates; keys are scoped, revocable, and
hashed at rest.

The API accepts the documented **PantryPress Publish Format**: a post with blocks, recipes, media references,
SEO fields, taxonomy, and an external source reference. It creates or updates a draft by that external
reference and can optionally publish or schedule it. It also reports status and the live URL, and can
unpublish.

Idempotency keys make retries safe. Media arrives by upload, or by HTTPS URL through the SSRF-safe fetcher.
The API goes through the same facades as the editor. PantryPress never calls CreatorPantry.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Inbound REST API with per-site keys (chosen) | A versioned contract and contract tests to maintain. No dependency on any caller. |
| PantryPress pulls from CreatorPantry | Simpler for CreatorPantry, but PantryPress would depend on and call CreatorPantry. |
| An OAuth app platform | Standard delegation, but marketplace-scale scope that is out of bounds for one developer. |
| A shared account or SSO between products | One sign-in, but couples identity across products, contrary to BIZ-010. |

## Consequences

- Keys are shown once, stored as salted SHA-256, compared in constant time, and never logged.
- Every write takes `Idempotency-Key`. Requests are rate-limited per key and per site, and audited.
- Validation, sanitization, wording preservation, entitlements, readiness checks, and malware scanning apply
  exactly as in the editor.
- OpenAPI and a contract test suite are published (API-009). `api-contract-checker` runs on every change.
- CreatorPantry builds its adapter in its own repository (requirements section 7.3).

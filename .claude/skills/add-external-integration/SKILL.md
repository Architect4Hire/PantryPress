---
name: add-external-integration
description: Add a typed gateway for an external provider with resilience, fakes, secret handling, webhooks, and no provider DTO leakage.
---

# Add an external integration

Read first: `.claude/rules/external.md`. Template F.

## Steps

1. Define the gateway interface in the owning module in domain terms.
2. Implement the provider client in an `Integrations/<Provider>/` folder; map DTOs at the boundary.
3. Add resilience (timeouts, retries, circuit breaker) and idempotency keys where supported.
4. Add a fake for local runs and tests; wire real or fake through Aspire parameters.
5. Webhooks: signature verification, raw storage, de-duplication, outbox processing.
6. Browser scripts: add to the PP-D11 allow-list behind a typed setting and to the per-site CSP only when
   enabled.

## Checklist

- [ ] No provider types outside the integration folder.
- [ ] Secrets from Key Vault or parameters only.
- [ ] Contract tests against recorded fixtures.
- [ ] `integration-reviewer` run.

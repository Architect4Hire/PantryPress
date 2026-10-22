# External integrations

Providers: Stripe, Azure Front Door (purge, custom domains), Azure Communication Services Email, malware
scanning, Google Safe Browsing, the spam challenge (default Cloudflare Turnstile), AdSense and allow-listed
ad networks, GA4 (client-side, consent-gated).

- One gateway per provider behind a typed interface in the owning module (ARCH-011). Use
  `add-external-integration`.
- Provider DTOs never cross the gateway. Map to domain types.
- Resilience from ServiceDefaults (timeouts, retries with jitter, circuit breaker); idempotency keys where
  the provider supports them.
- Provider ids live only in integration records (`StripeCustomerId` on a billing record, not on `Account`).
- Webhooks: verify signatures, store raw events, de-duplicate by event id, process through the outbox
  idempotently (BILL-002).
- Secrets come from Key Vault or Aspire parameters. Never in code, config files, or logs.
- Every provider has a fake for local runs and tests. `aspire run` works with no external accounts through
  PP8.
- Third-party browser scripts only from the PP-D11 allow-list, each behind a typed setting, each adding its
  origins to the per-site CSP only when enabled (MON-005).
- `integration-reviewer` runs after any integration prompt.

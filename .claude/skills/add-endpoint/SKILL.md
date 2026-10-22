---
name: add-endpoint
description: Add an authenticated admin or publishing API endpoint in PantryPress.ApiService through the Controller → Facade → Business → DataLayer → Repository | Gateway seam.
---

# Add an authenticated endpoint

Read first: `.claude/rules/backend.md`, `api-contract.md`, `auth.md`, `tenancy.md`.

## Steps

1. Name the use case and the requirement IDs. Pick the module under `PantryPress.Domain/Modules/<Context>/`.
2. Define request and response records in the module's `Contracts/`. Never expose entities.
3. Add or extend the validator (FluentValidation) with plain-language messages.
4. Add the facade method: resolve site context, check resource ownership, open the transaction, call
   business and data layer, write outbox events and audit, return `Result<T>`.
5. Add business rules as pure methods with unit tests.
6. Add repository queries; rely on the global site filter; never call `IgnoreQueryFilters()`.
7. Add the thin controller action with the route `/api/v1/sites/{siteSlug}/…` (or `/api/publishing/v1/…`),
   the named policy, idempotency for retryable writes, and problem-details mapping.
8. Register the route in the gateway configuration if it is an admin route.

## Checklist

- [ ] Integration test through the controller.
- [ ] Two-site test for any site-owned data.
- [ ] Role tests: Owner, Editor, Author, non-member.
- [ ] Entitlement check through the facade if the feature is gated.
- [ ] OpenAPI updated; `api-contract-checker` run.

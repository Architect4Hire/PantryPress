# Authentication and authorization

**Detail: `docs/requirements/pantrypress-auth-spec.md`** (the ticket, second step, session end, site policies, members).
Read it before changing sign-in, sessions, roles, or keys.

## Accounts

- ASP.NET Core Identity in `Modules/Accounts`. Email verification before publishing (ACCT-001) and before owning more
  than one site. Optional TOTP, required for `PlatformAdmin`; a code works once. Lockout after 5 wrong attempts for
  15 minutes (429 with `Retry-After`). Disposable-email blocking at signup.

## Browser boundary (BFF)

- The admin SPA never holds tokens; `PantryPress.Gateway` holds the session on the apex (PP-D19). Cookie
  `__Host-pp-session`: host-only, `Secure`, `HttpOnly`, `SameSite=Lax` or stricter. **No cookie is ever scoped to
  `.pantrypress.app`.** No cross-host sign-in handoff; email links open `Accounts:AppBaseUrl`.
- Antiforgery on every state-changing admin request. The gateway strips inbound identity headers and adds its own.
- The ticket holds an account id and security stamp, never a token. ApiService believes the forwarded headers only
  behind the shared secret, reloads the account and roles every request, and answers a stale session 401
  `session-ended`. The second step is bound to the first by the stamp. Sessions have their own id, are revoked on sign
  out, and end 12 hours after sign-in regardless of the 8-hour sliding window.

## Roles and policies

| Role | Scope | Can |
| --- | --- | --- |
| Owner | site | Everything, including billing, members, domains, publishing keys |
| Editor | site | All content, design, and moderation |
| Author | site | Own posts and own recipes only; cannot publish when review is required (POST-010) |
| PlatformAdmin | platform | Operator console only, audited; only for a confirmed address |

- Named policies (`SiteOwner`, `SiteEditor`, `SiteAuthor`, `PlatformAdmin`) in controllers; facades re-check resource
  ownership. Site roles come from `SiteMembers`, never Identity roles; each covers those below it. Suspended members
  reach nothing. Every site keeps an active Owner (`last-owner`, 409).
- A site policy decides from `{siteSlug}` and the account, via the Sites facade. No site, deleted, not a member, and
  suspended are one answer: 404 `site-not-found`; too low a role is 403 `site-role-required`. The host refuses to start
  on a `{siteSlug}` route without a site policy, or the reverse.
- Site lookups are rate-limited per account before the policy runs. Membership is cached per request only.

## Publishing keys (PP-D15, PSEC-008)

- Created by Owners, shown once, stored hashed (SHA-256, per-key salt), scoped (`posts:write`, `media:write`,
  `posts:publish`), revocable, rate-limited, never logged. Sent as `Authorization: Bearer pp_live_…`; prefix for lookup,
  the rest compared in constant time.

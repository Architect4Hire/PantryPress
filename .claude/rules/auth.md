# Authentication and authorization

## Accounts

- ASP.NET Core Identity in `Modules/Accounts`. Email verification required before publishing (ACCT-001).
- Optional TOTP two-factor; required for `PlatformAdmin`.
- Disposable-email blocking at signup (ACCT-003 default yes).

## Browser boundary (BFF)

- The admin SPA never holds tokens. `PantryPress.Gateway` (YARP) holds the session.
- Session cookie: `__Host-pp-session`, host-only, `Secure`, `HttpOnly`, `SameSite=Lax` or stricter (PP-D13,
  PSEC-003). Tenant sites run third-party ad scripts on sibling subdomains, so no cookie may be scoped to
  `.pantrypress.app`.
- Antiforgery on every state-changing admin request.
- The gateway strips inbound identity headers and adds its own.

## Roles and policies

| Role | Scope | Can |
| --- | --- | --- |
| Owner | site | Everything, including billing, members, domains, publishing keys |
| Editor | site | All content, design, and moderation |
| Author | site | Own posts only; cannot publish when review is required (POST-010) |
| PlatformAdmin | platform | Operator console only, audited |

- Policies are named (`SiteOwner`, `SiteEditor`, `SiteAuthor`, `PlatformAdmin`) and checked in controllers;
  facades re-check resource ownership (an Author editing someone else's post).
- Membership lookups are cached per request, never across requests without the site id in the key.

## Publishing keys (PP-D15, PSEC-008)

- Created by Owners, shown once, stored hashed (SHA-256 with a per-key salt), scoped (`posts:write`,
  `media:write`, `posts:publish`), revocable, rate-limited, never logged.
- Sent as `Authorization: Bearer pp_live_…`. Key prefix is stored for lookup; the rest is compared in
  constant time.

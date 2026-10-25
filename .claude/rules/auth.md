# Authentication and authorization

## Accounts

- ASP.NET Core Identity in `Modules/Accounts`. Email verification required before publishing (ACCT-001).
- Optional TOTP two-factor; required for `PlatformAdmin`. Setting it up needs a confirmed email address (PP1.12), and a
  reset that confirms an address for the first time clears a second factor the account already had. An authenticator
  code works once; the same right code sent again is refused (`code-already-used`) without counting toward lockout.
- Lockout after 5 wrong passwords or codes, for 15 minutes; its 429 carries `Retry-After` (PP1.12).
- Disposable-email blocking at signup (ACCT-003 default yes).

## Browser boundary (BFF)

- The admin SPA never holds tokens. `PantryPress.Gateway` (YARP) holds the session.
- Session cookie: `__Host-pp-session`, host-only, `Secure`, `HttpOnly`, `SameSite=Lax` or stricter (PP-D13,
  PSEC-003). Tenant sites run third-party ad scripts on sibling subdomains, so no cookie may be scoped to
  `.pantrypress.app`.
- Antiforgery on every state-changing admin request.
- The gateway strips inbound identity headers and adds its own.
- **What the ticket holds (PP1.6):** an account id and that account's Identity security stamp - never a token. The
  gateway forwards both as `X-PP-Account` and `X-PP-Stamp` behind `X-PP-Gateway`, a secret shared with ApiService
  alone and compared in constant time; `X-PP-Mfa` says the second step was passed. The headers carry no authority of
  their own: ApiService reloads the account on every request, compares the stamp, and builds the claims - roles
  included - from the database, so a changed password, a changed second factor, or a revoked role ends a session at
  its next request. A stale session gets 401 `session-ended`, which tells the gateway to clear its cookie. A route
  that rotates the stamp hands the new one back in `X-PP-New-Stamp`, so the caller's own session carries on while the
  account's others end.
- **The second step is bound to the first:** the half-finished ticket carries the stamp, and the second-factor check
  refuses a code whose stamp does not match before counting any attempt, so a code alone never completes a sign-in.
- **A session ends when it is meant to (PP1.12).** Each complete session has its own random id (`X-PP-Sid`) and its
  sign-in time in the ticket. Signing out revokes the id at the API (`RevokedSessions`, remembered 24 hours, purged by the
  Worker), so a copy of the cookie stops working wherever it is presented; and a session ends 12 hours after sign-in
  however busy it is kept, on top of the 8-hour sliding window. The second-step claim counts only while the account still
  has two-step sign-in, so an operator who turns it off stops meeting the `PlatformAdmin` policy at once.

## Roles and policies

| Role | Scope | Can |
| --- | --- | --- |
| Owner | site | Everything, including billing, members, domains, publishing keys |
| Editor | site | All content, design, and moderation |
| Author | site | Own posts only; cannot publish when review is required (POST-010) |
| PlatformAdmin | platform | Operator console only, audited; granted only to an account that confirmed its address (PP1.12) |

- Policies are named (`SiteOwner`, `SiteEditor`, `SiteAuthor`, `PlatformAdmin`) and checked in controllers;
  facades re-check resource ownership (an Author editing someone else's post).
- Site roles come from `SiteMembers` (PP2.3), never from Identity roles. Each role covers the ones below it. A membership
  is Active or Suspended; a suspended member keeps the role and reaches nothing. Every site keeps an active Owner: the last
  one cannot be demoted, suspended, or removed, and the database refuses it too (`tenancy.md`, "Tenant root").
- A site policy decides by the route's `{siteSlug}` and the signed-in account, in the authorization middleware ahead of
  the idempotency step; the decision is the Sites facade's, never the host's. No such site, a deleted site, no membership,
  and a suspended one are one answer, 404 `site-not-found`; a member whose role is too low gets 403 `site-role-required`.
  The host refuses to start when a `{siteSlug}` route lacks a site policy, or a site policy sits anywhere else.
- Membership lookups are cached for one request only, keyed by account and slug, and never across requests.

## Publishing keys (PP-D15, PSEC-008)

- Created by Owners, shown once, stored hashed (SHA-256 with a per-key salt), scoped (`posts:write`,
  `media:write`, `posts:publish`), revocable, rate-limited, never logged.
- Sent as `Authorization: Bearer pp_live_…`. Key prefix is stored for lookup; the rest is compared in
  constant time.

# 0013 — Domains, cookies, and the edge

- **Status:** Accepted, with two open items
- **Date:** 2026-10-22
- **Decided by:** Owner (Front Door Standard); remainder confirmed at PP0.1
- **Decision ID:** PP-D13
- **Requirements:** DOM-001 to DOM-005, PSEC-001 to PSEC-003, SITE-003; requirements section 1.2 (Edge, Custom
  domains)
- **Rules affected:** .claude/rules/auth.md, .claude/rules/gateway.md, .claude/rules/sites.md

## Context

Every site lives on a sibling subdomain of `pantrypress.app`, and many of them run third-party ad scripts
(ADR 0011). The admin app lives on the same registrable domain at `app.pantrypress.app`. Any cookie scoped to
the parent domain would be sent to every tenant site. A sibling subdomain could also set parent-domain cookies
of its own ("cookie tossing"). Front Door Premium adds managed WAF rule sets, a bot rule set, and Private
Link, but its base fee is significant at launch.

## Decision

- `*.pantrypress.app` is a wildcard DNS record pointing at Front Door Standard with a wildcard certificate.
  Every site always keeps its subdomain. The admin app is at `app.pantrypress.app`.
- Custom domains are opt-in for paid plans. They use Front Door custom domains with managed certificates and
  are verified by TXT record. While a custom domain is attached it is canonical, and the subdomain
  301-redirects to it.
- The BFF session cookie is `__Host-pp-session`: host-only, `Secure`, `HttpOnly`, and `SameSite=Lax` or
  stricter. `pantrypress.app` is submitted to the Public Suffix List.
- The edge is Front Door **Standard**. Its missing protections are compensated for by WAF custom rules (rate
  limits, geo and IP blocks), origins locked to the `AzureFrontDoor.Backend` service tag plus an
  `X-Azure-FDID` check, and application-level defenses.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Front Door Standard with custom WAF rules (chosen) | No managed rule sets or bot protection, so more application-side defense. Low base fee. |
| Front Door Premium | Managed WAF rules, bot protection, and Private Link, but a much higher base fee before revenue. |
| A parent-domain session cookie | Simpler sign-in across subdomains, but the cookie reaches every tenant site and is exposed to cookie tossing. |
| Custom domains for every plan | More reach for Starter, but more certificate and abuse load. |

## Consequences

- No cookie is ever scoped to `.pantrypress.app`.
- The subdomain always keeps working, including after a custom domain is removed.
- Origins reject traffic that did not come through this Front Door profile.
- Public sites and the admin app share the Front Door profile but have separate endpoints, routes, and WAF
  policies (ADR 0016).

## Open items

- **D-13:** the trigger for upgrading to Premium. Default assumed: a sustained attack the custom rules cannot
  handle, or revenue that makes the Premium base fee small. Operational; no build impact. See the
  [register](pantrypress.md#3-open-decide-items).
- **D-13b** (opened 2026-10-24): custom domains beyond one profile. Front Door Standard allows 100 custom
  domains and 10 endpoints per profile, and the wildcard, `app.`, CreatorPantry's domains, and every opt-in
  custom domain count against them. Default assumed: add a second Standard profile, or move to Premium (500 per
  profile) if D-13 has already triggered, before PantryPress's custom domains on a profile pass 80; PP17.1 checks
  the platform-wide count. Closes at PP17.1. See the [register](pantrypress.md#3-open-decide-items).

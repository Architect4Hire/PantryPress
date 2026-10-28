# 0019 — The platform home: one address for the landing page, sign-in, and the dashboard

- **Status:** Accepted
- **Date:** 2026-10-28
- **Decided by:** Owner
- **Decision ID:** PP-D19
- **Supersedes:** ADR 0013 in part: the admin host only ("The admin app is at `app.pantrypress.app`"). Everything
  else in 0013 stands.
- **Amends:** PP-D08's confirmation that the admin app is client-rendered: its public routes are prerendered at build.
- **Requirements:** PSEC-002, PSEC-003, SITE-009, BIZ-009, BIZ-010; PPL-001 to PPL-012
  (`docs/prompts/pantrypress-landing-page-scrub-prompts.md`)
- **Rules affected:** CLAUDE.md, .claude/rules/auth.md, gateway.md, aspire.md, frontend.md, sites.md, tenancy.md,
  design-system.md

## Context

PantryPress needs a public landing page, modelled on CreatorPantry's, at the root of `pantrypress.app`. The owner
wants sign-in and sign-up to be routes off that root, and a signed-in visitor to go straight into the app.

The session cookie is `__Host-pp-session`: host-only, with no `Domain` (ADR 0013). A cookie set at
`pantrypress.app/sign-in` is never sent to `app.pantrypress.app`, and one set at `app.pantrypress.app` is never sent
to the bare address. So a sign-in page and the app it signs into must share one host, unless a handoff is built
between them. The Terraform had left the zone's apex "for the marketing site, not for this stack" (`dns.tf`), and no
prompt built one.

## Decision

- **`pantrypress.app` (the zone's apex) is PantryPress's own address.** It serves, from one origin:
  - the landing page at `/`, the same page opened at its pricing section at `/pricing`, and the platform legal pages
    (`/terms`, `/privacy`, `/acceptable-use`, `/dmca`; content from PP19.13);
  - `/sign-in`, `/sign-up`, `/verify-email`, `/reset-password`, and the invitation landing that account and
    invitation emails link to;
  - the signed-in app under `/dashboard/…`;
  - the admin API at `/api/v1/…`, exactly as before.
- **`PantryPress.Gateway` serves the apex.** `Gateway:Hosts` names the apex of each environment: `pantrypress.app`
  in production, `dev.pantrypress.app` in dev, and `localhost` in local Development (its own port; sites stay at
  `{slug}.localhost` on the Site resource's port, SITE-009). Account and invitation emails link to that origin
  (`Accounts:AppBaseUrl`).
- **`app.pantrypress.app` and `www.pantrypress.app` permanently redirect (301) to the apex** at Front Door, path and
  query kept. Both stay reserved slugs. Admin v1 has no client outside this repository yet (`api-contract.md`), so no
  live link breaks.
- **One Angular application builds every page.** `pantrypress-admin` owns the whole apex: the public routes (`/`,
  `/pricing`, the legal pages) are **prerendered at build** to static HTML, so scrapers and readers without
  JavaScript get a complete page, and no Node server runs for the apex. Every other route stays client-rendered, as
  PP-D08 confirmed for the dashboard. No price, plan figure, or account data is baked into prerendered HTML. The
  pricing section reads the server's catalogue in the browser and renders nothing without it.
- **A signed-in visitor never sees marketing.** The gateway answers a `GET` of `/` or `/pricing` that carries a
  complete session ticket with a 302 to `/dashboard`, before any HTML. It reads only its own cookie for this and asks
  ApiService nothing; the dashboard's guard then confirms the session (`sessions/current`). This replaces the
  client-side "anonymous-only" guard CreatorPantry needed.
- **Documents on the apex are never cached at the edge**, because `/` answers differently for a signed-in visitor.
  The apex route caches only fingerprinted static assets. The gateway answers its documents `Cache-Control: no-cache`.
- **Cookies are unchanged in kind.** Every cookie the gateway sets is still `__Host-` and host-only, now on the
  apex. A host-only cookie on `pantrypress.app` is never sent to `{slug}.pantrypress.app`, so tenant sites and their
  ad scripts still cannot read or receive it.
- **HSTS from the apex covers the whole zone.** The gateway's one-year `includeSubDomains` header, sent from
  `pantrypress.app`, pins every host under the zone to HTTPS: every blog subdomain, `dev.` and its blogs, and any
  future name. Every web host under the zone must therefore serve HTTPS, which the Front Door wildcard already does.
  Still no `preload`. Custom domains are not under the zone and are unaffected.

## Options considered

| Option | Trade-off |
| --- | --- |
| One address: the landing page, auth screens, and `/dashboard` on the apex (chosen) | One cookie, one Angular app, sign-in really is a route off the root, and a signed-in visitor is redirected by the gateway before any paint. The apex becomes the product host, so its HSTS covers the zone, and its documents cannot be edge-cached. |
| The app on its own subdomain (`my.pantrypress.app`), the landing page on the apex | Keeps the product host separate from the zone apex. Sign-in must then live on the subdomain (the cookie is host-only), so `/sign-in` on the apex is only a redirect, and the marketing page and the app are two Angular apps. |
| Auth screens on the apex, the app on a subdomain, a sealed one-time handoff code | Gives both addresses the owner first described, at the cost of a new sign-in surface (code leakage, login CSRF, replay) that ADR 0013 avoided. |
| A separate marketing site outside this stack | Fast to host, but a second build, a second design system copy, and no signed-in redirect. |

## Consequences

- `/dashboard` is the app's root path. A signed-in visitor at `/` or `/pricing` lands there; an anonymous visitor at
  `/dashboard/…` is sent to `/sign-in`, as PP3.8's guards do.
- The public paths on the apex are a closed list owned by the Angular route table. Anything else that is not
  `/api/…` is the app's own not-found page, never a tenant site: the apex is not a tenant, and the Site host answers
  it with the neutral 404 (`tenancy.md` rule 5).
- The landing page shares the gateway's content-security policy and response headers (PSEC-002). It loads no
  third-party script; fonts are self-hosted.
- Public sign-up still waits for the abuse controls (BIZ-009). Until they ship, the landing page says sign-up opens
  at launch (PPL.13).
- Front Door gains the apex, `www`, and `app` as custom domains on the PantryPress endpoint for the gateway. They
  count toward Standard's 100 per profile (D-13b): one more than before (`app` already counted).
- The Public Suffix List entry for `pantrypress.app` (ADR 0013) makes the apex itself a public suffix. Host-only
  cookies (no `Domain` attribute) are not subject to the public-suffix check, so `__Host-` cookies there should be
  unaffected. PP9.7 proves it in Chromium, Firefox, and WebKit before the entry is submitted.
- The code moves in PPL.1 (gateway hosts, email origin, local host, tests), PPL.2 (the Angular route table and
  prerendering), and PPL.3 (the gateway's redirect and document headers). The DNS apex alias and the Front Door
  routes move in PP9.1a and PP9.2.
- The publishing API's edge host (architecture O-01) is still open, and this ADR does not decide it.

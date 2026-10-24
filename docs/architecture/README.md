# PantryPress Architecture

This folder holds the PantryPress design document. Each decision behind the design is recorded as an
architecture decision record (ADR) in [`../architecture-decisions/`](../architecture-decisions/). ADR `NNNN`
records decision `PP-DNN`.

## Design

| Document | What it holds |
| --- | --- |
| [PantryPress design](architecture.md) | How the system is built: solution layout, the request seam, site isolation, the public tier, and how the parts fit together |
| [Provisioning a site at sign-up](tenant-provisioning.md) | What sign-up creates automatically, why it is data rather than infrastructure, the provisioning job, custom domains, and the lifecycle in reverse |

## Architecture decision records

The [decisions record](../architecture-decisions/pantrypress.md) is the index. It holds each ADR's status, the
owner decisions, and every open DECIDE item with its assumed default.

| ADR | Decision |
| --- | --- |
| [0001](../architecture-decisions/0001-own-repository-and-aspire-application.md) | Own repository and Aspire application |
| [0002](../architecture-decisions/0002-cache-first-ssr-delivery.md) | Cache-first delivery with Angular SSR on every request |
| [0003](../architecture-decisions/0003-site-is-the-tenant.md) | The site is the tenant |
| [0004](../architecture-decisions/0004-publishing-is-a-snapshot.md) | Publishing is a snapshot |
| [0005](../architecture-decisions/0005-structured-recipes-deterministic-calculations.md) | Structured recipes and deterministic calculations |
| [0006](../architecture-decisions/0006-data-zones.md) | Data zones |
| [0007](../architecture-decisions/0007-pantrypress-design-system.md) | A new design system, `@pantrypress/ui` |
| [0008](../architecture-decisions/0008-public-site-angular-ssr.md) | Public themes are an Angular 22 SSR application |
| [0009](../architecture-decisions/0009-stripe-subscriptions-in-house-entitlements.md) | Subscriptions via Stripe, entitlements in-house |
| [0010](../architecture-decisions/0010-trial-instead-of-free-tier.md) | Trial instead of a free-forever tier |
| [0011](../architecture-decisions/0011-third-party-script-allow-list.md) | Third-party scripts are an allow-list |
| [0012](../architecture-decisions/0012-no-ai-in-pantrypress.md) | No AI anywhere in PantryPress |
| [0013](../architecture-decisions/0013-domains-cookies-and-edge.md) | Domains, cookies, and the edge |
| [0014](../architecture-decisions/0014-newsletter-signup-and-export-only.md) | Newsletter signup and CSV export only |
| [0015](../architecture-decisions/0015-publishing-api.md) | The publishing API is how other tools publish into PantryPress |
| [0016](../architecture-decisions/0016-shared-azure-separate-data.md) | Shared Azure, separate data |
| [0017](../architecture-decisions/0017-studio-site-customization.md) | Studio site customization |
| [0018](../architecture-decisions/0018-section-based-pages.md) | Section-based pages, no page builder |

## Related

- [Architecture entry point](../architecture.md)
- [Requirements](../requirements/pantrypress-requirements.md), the functional source of truth
- [Customization spec](../requirements/pantrypress-customization-spec.md)

If the design document and an ADR disagree, stop and reconcile them before writing code.

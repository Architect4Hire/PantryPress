# 0016 — Shared Azure, separate data

- **Status:** Accepted (D-16 closed 2026-10-24)
- **Date:** 2026-10-22
- **Decided by:** Owner
- **Decision ID:** PP-D16
- **Requirements:** OPS-001, KPI-006; requirements section 1.2 (Azure)
- **Rules affected:** .claude/rules/aspire.md, .claude/rules/external.md

## Context

Some Azure resources carry a fixed base cost or operational overhead that is paid once per instance. Front
Door is the clearest example: the profile has a base fee. Sharing those with CreatorPantry keeps
infrastructure cost per paying site low (KPI-006). Data stores are a different matter. Sharing them would
couple the products' load, incidents, credentials, and lifecycles.

## Decision

PantryPress has its own Azure SQL server, Redis, storage account, Key Vault, Communication Services, and DNS
zone. It shares only:

- the Front Door Standard **profile**, with separate endpoints, routes, and WAF policies;
- the Container Apps **environment**, with separate apps and managed identities;
- the Log Analytics and Application Insights **workspace**, with separate cloud role names.

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| Share the edge, compute environment, and monitoring; separate data (chosen) | The two products' IaC must coordinate. One base fee, isolated data. |
| Fully separate Azure resources | The cleanest boundary, but pays the Front Door base fee twice and duplicates operations. |
| Share data stores as well | Cheapest, but one product's load, incident, or leaked credential reaches the other. Breaks independence. |

## Consequences

- Locally, `aspire run` shares nothing; every resource is PantryPress's own.
- Sharing exists only in IaC. No code in either product references the other's resources.
- PantryPress identities have no access to CreatorPantry data stores, and the reverse.

## Open items

- **D-16:** who owns the shared resources in IaC. Default assumed: an `infra/platform` module in this
  repository, with CreatorPantry's IaC referencing them as existing resources. Closes at PP19. See the
  [register](pantrypress.md#3-open-decide-items).
  - **Outcome (2026-10-24, owner):** this repository owns them, in the Terraform `platform` stack at
    `docs/infrastructure/terraform/platform/` (OPS-001 closed as Terraform). There is one set per environment, so
    dev and prod each have their own Front Door profile, Container Apps environment, and workspace, and both
    products share each set. CreatorPantry's IaC looks them up by name, exactly as PantryPress's own stack does.

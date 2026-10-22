# PantryPress Claude Code toolkit

How Claude Code works in this repository. `CLAUDE.md` is the constitution; this folder holds the detail.

## Which files apply to which change

| Change | Rules | Skill | Reviewers |
| --- | --- | --- | --- |
| Admin or publishing API endpoint | backend, api-contract, auth, tenancy | add-endpoint | architecture-reviewer, api-contract-checker |
| New site-owned table | tenancy, backend | add-site-entity | site-isolation-auditor |
| Anonymous reader endpoint | public-endpoints, tenancy | add-public-endpoint | site-isolation-auditor, public-site-reviewer |
| Recipe parsing, scaling, conversion, JSON-LD | recipes, content | add-recipe-feature | test-gap-analyzer |
| Editor block, section block, page block | content, rendering | add-editor-block | public-site-reviewer, design-review |
| Upload, derivatives, media metadata | media, external | add-media-feature | site-isolation-auditor |
| Worker job | backend, tenancy, rendering | add-background-job | architecture-reviewer |
| Stripe, email, Front Door, scanning, ads | external, billing | add-external-integration | integration-reviewer |
| Public component or design option | rendering, design-system, sites | add-theme-component | public-site-reviewer, design-review |
| Page data, home, archives, feeds, search | rendering, content | add-render-artifact | public-site-reviewer, site-isolation-auditor |
| Admin screen | frontend, design-system | add-admin-component | design-review |
| AppHost resource | aspire | add-aspire-resource | architecture-reviewer |
| Publishing API | publishing-api, api-contract, auth | add-endpoint | api-contract-checker |
| Plans and limits | billing | add-endpoint | architecture-reviewer |
| Sites, domains, design settings, pages | sites, tenancy | (varies) | site-isolation-auditor |

`test-gap-analyzer` can run after any prompt.

## Folders

- `rules/`: 17 rule files, one per area. Each stays under 150 lines and cross-references the others.
- `skills/`: 12 procedures, one per kind of seam. Procedure and checklist only, no product code.
- `agents/`: 7 read-only reviewers (Read, Grep, Glob). Findings need file and line evidence.
- `hooks/`: Node scripts, no dependencies, so they run on Windows, macOS, and Linux.
  - `secret-scan.mjs` (before writes): blocks content that looks like a real secret.
  - `invariant-guard.mjs` (before writes): blocks AI SDKs, `IgnoreQueryFilters()` outside platform code,
    interpolated raw SQL, browser storage in the SSR site, and DbContext use on public read paths.
  - `format-guard.mjs` (after writes): runs Prettier on `web/` files and `dotnet format whitespace` on C#
    files once the solution exists. Best effort; it never fails an edit.
- `settings.json`: shared permissions and hook wiring. Put personal overrides in `settings.local.json`
  (git-ignored).

## Requirements

Node.js 18 or later on the path for the hooks. Everything else arrives with PP0.

## Changing the toolkit

PP0.2 to PP0.6 own these files. Change a rule when a decision changes, record the decision first, and keep
CLAUDE.md under 250 lines.

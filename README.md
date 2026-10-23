# PantryPress

<div>
 <img src="docs/images/logo.png" />
</div>

A hosted food-blogging platform with everything built in. Bloggers write posts and structured recipes and
publish them to a fast, search-friendly site with a real recipe card, ratings, comments, a subscriber list,
ads, and analytics. There are no plugins, no theme hunting, no updates, and no servers to run.

Every site gets `{name}.pantrypress.app`. Paid plans can add a custom domain.

> **Status:** pre-build. This repository starts from the requirements, design spec, and SCRUB prompt plan
> in `docs/`. Code arrives phase by phase, starting with PP0.

## What a blogger gets

- **Writing:** a block editor, structured recipes with paste-to-parse, revisions, preview, scheduling.
- **Recipe card:** jump to recipe, print view, servings scaling, US and metric, cook mode, schema.org
  JSON-LD for rich results.
- **Site design:** six presets and a customizer for brand color, fonts, shapes, headers, hero images,
  recipe card styles, and section order. Contrast is checked automatically. No code needed or allowed.
- **Pages:** a homepage and site pages built from ready-made sections and blocks, with About, Start here,
  and Contact templates.
- **Audience:** ratings and reviews, moderated comments, newsletter signup with CSV export, contact form.
- **Earning:** AdSense on Plus and Pro, allow-listed ad networks on Pro, affiliate links with automatic
  disclosure.
- **Insight:** cookie-free analytics; optional GA4 after consent.
- **Connections:** a publishing API for tools such as CreatorPantry, and WordPress import.

## How it works

```text
Readers ─► Azure Front Door (cache, TLS, WAF) ─► pantrypress-site (Angular SSR) ─► PantryPress.Site API ─► Redis / Blob page data
Bloggers ─► app.pantrypress.app ─► PantryPress.Gateway (BFF) ─► PantryPress.ApiService ─► Domain ─► Azure SQL
                                                                   ▲
                                         CreatorPantry (optional) ─┘ publishing API, per-site keys
PantryPress.Worker: page-data builds, media derivatives, purges, sitemaps, feeds, search, imports, webhooks
```

Publishing creates an immutable snapshot. The worker builds versioned page data from it and switches one
pointer. Public pages are rendered by Angular SSR from that page data, cached at the edge, and never touch
SQL, so reader traffic survives a database outage.

## Tech stack

| Area          | Choice                                                          |
| ------------- | --------------------------------------------------------------- |
| Back end      | .NET 10, ASP.NET Core, Aspire, EF Core 10                       |
| Data          | Azure SQL, Redis, Azure Blob Storage (all PantryPress's own)    |
| Front end     | Angular 22: admin SPA, public site with SSR on every request    |
| Design system | `@pantrypress/ui` (admin) and `@pantrypress/ui/themes` (public) |
| Edge          | Azure Front Door Standard                                       |
| Auth          | ASP.NET Core Identity behind a YARP BFF with `__Host-` cookies  |
| Billing       | Stripe Checkout, customer portal, Stripe Tax                    |
| Email         | Azure Communication Services (transactional only)               |

## Repository layout

```text
.claude/            Claude Code toolkit: rules, skills, reviewer agents, hooks
docs/
  requirements/     pantrypress-requirements.md, pantrypress-customization-spec.md
  prompts/          pantrypress-scrub-prompts.md (the build plan)
  design/mockups/   reader-view and Studio customizer mockups (open in a browser)
src/                .NET solution (created in PP0.3)
  web/              Angular workspace (created in PP0.7)
CLAUDE.md           the constitution Claude Code follows
```

## Getting started

Prerequisites: .NET 10 SDK, Node.js 24 LTS with npm, a Docker-compatible runtime, the Aspire CLI, and Claude
Code.

Before any code exists:

1. Open the repository in Claude Code.
2. Run prompt **PP0.1** from `docs/prompts/pantrypress-scrub-prompts.md` to write the decisions record.
3. Continue one prompt at a time. Review each plan before approving it.

PP0.2 refines the toolkit already in `.claude/`. Treat that prompt as a review of these files against the
decisions record rather than a rewrite.

Once PP0.8 is done:

```bash
aspire run
```

The Aspire dashboard shows every resource. The admin app runs at `app.localhost` and sites at
`{slug}.localhost`.

## Building with Claude Code

- The build plan has 21 phases (PP0 to PP20) of small SCRUB prompts: Scope, Constraint, Restriction,
  Usage, Behavior.
- Run one prompt at a time, plan first, and mark it done only after its verification passes.
- Checkpoints after each phase are listed at the top of the prompt file.
- `.claude/README.md` maps the toolkit: which rule, skill, and reviewer applies to which kind of change.

## Principles

- **Closed feature set.** If it is not in requirements section 2.4, it is out of scope until the list is
  amended.
- **No AI in PantryPress.** Deterministic code only.
- **The site is the tenant.** Every site-scoped feature proves isolation with a two-site test.
- **Cache first.** Public pages keep working when SQL, the admin API, or the worker is down.
- **Accessible and fast.** WCAG 2.2 AA in every design combination; under 100 KB of first-party CSS and
  JavaScript per page.
- **Easy as pie.** Bloggers never see DNS, caches, or HTML unless they opt into a custom domain.

## License

Proprietary. All rights reserved.

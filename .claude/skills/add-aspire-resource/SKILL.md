---
name: add-aspire-resource
description: Add or change a resource or project in the Aspire AppHost with references, WaitFor ordering, parameters, health checks, and local fakes.
---

# Add an Aspire resource

Read first: `.claude/rules/aspire.md`.

## Steps

1. Declare the resource in `PantryPress.AppHost` with a clear name.
2. Wire references and `WaitFor` (everything waits for the migration service).
3. Secrets and external endpoints as Aspire parameters, never literals.
4. Add health checks and make sure the dashboard shows the resource healthy.
5. Keep PantryPress data resources its own (PP-D16); shared Azure resources appear only in IaC.

## Checklist

- [ ] `aspire run` starts cleanly from a fresh clone.
- [ ] Integration tests can start the resource or its emulator.
- [ ] README or CLAUDE.md usage updated if commands changed.

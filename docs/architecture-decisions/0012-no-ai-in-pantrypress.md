# 0012 — No AI anywhere in PantryPress

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decided by:** Confirmed at PP0.1
- **Decision ID:** PP-D12
- **Requirements:** ARCH-013; requirements section 1.3 ("Not an AI writing tool")
- **Rules affected:** CLAUDE.md, .claude/hooks/invariant-guard.mjs

## Context

CreatorPantry is the AI product. PantryPress promises deterministic rendering, parsing, and moderation,
predictable costs for a team of one, and no reader or blogger data sent to model providers. AI features would
duplicate CreatorPantry and introduce outputs that tests cannot pin down.

## Decision

PantryPress makes no AI or model call anywhere. Rendering, editing, ingredient parsing, and moderation are
deterministic code. AI-assisted recipe development and writing belong to CreatorPantry, which can publish
into PantryPress through the publishing API (ADR 0015).

## Options considered

| Option | Cost / trade-off |
| --- | --- |
| No AI (chosen) | No writing assistance inside PantryPress. Deterministic, testable, no model cost. |
| AI writing aids in the editor | Competitive with other blog tools, but duplicates CreatorPantry, adds per-use cost, and output is non-deterministic. |
| AI comment moderation or ingredient parsing | Less manual work, but results are non-reproducible, and reader and blogger text goes to a provider. |

## Consequences

- The invariant-guard hook blocks AI SDK references in `.csproj`, `package.json`, and
  `Directory.Packages.props`, and AI SDK imports and model-provider endpoints in source under `src/`
  (widened at PP0.2).
- Spam defense uses deterministic rules and the spam challenge (ENG-003).
- Parsing quality comes from rules and golden corpora (ADR 0005).

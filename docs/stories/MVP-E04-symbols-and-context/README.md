# MVP-E04 — Symbols, Identity, and Bounded Context

## Outcome

Agents can discover a declaration, resolve it safely across processes, verify
candidate syntax semantically, and retrieve bounded source context.

## Scope

- Ranked symbol declaration discovery and candidate-scoped semantic loading.
- Syntax candidate verification with explicit verified, rejected, and
  unresolved counts.
- Stateless entity IDs, owner/framework variants, and stale-ID handling.
- Symbol/document show, outline, and budgeted context composition.

## Boundary

References, implementations, call relationships, and graph traversal belong to
`MVP-E05`.

## Design

- [Search and context](../../design/search-and-context.md)
- [Workspace](../../design/workspace.md)
- [Design foundations](../../design/foundations.md)
- [CLI and output contract](../../design/output-contract.md)

## Dependencies

- `MVP-E02`
- `MVP-E03`

## Stories

- [MVP-E04-S01 — Search symbol declarations](MVP-E04-S01-symbol-search.md)
- [MVP-E04-S02 — Create stateless entity identity](MVP-E04-S02-entity-identity.md)
- [MVP-E04-S03 — Protect stale and variant identity](MVP-E04-S03-stale-and-variant-identity.md)
- [MVP-E04-S04 — Verify syntax candidates semantically](MVP-E04-S04-semantic-candidate-verification.md)
- [MVP-E04-S05 — Show a symbol](MVP-E04-S05-show-symbol.md)
- [MVP-E04-S06 — Show a document](MVP-E04-S06-show-document.md)
- [MVP-E04-S07 — Enforce context budgets](MVP-E04-S07-context-budgets.md)
- [MVP-E04-S08 — Compose symbol context](MVP-E04-S08-symbol-context.md)
- [MVP-E04-S09 — Outline source](MVP-E04-S09-outline.md)

## Complete when

- Unchanged entity IDs resolve after cache deletion and never silently bind to
  a different declaration.
- Context results stay within budget and preserve identity, location,
  resolution, scope, coverage, and provenance.

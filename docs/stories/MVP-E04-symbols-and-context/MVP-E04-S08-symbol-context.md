# MVP-E04-S08 — Compose Symbol Context

## Outcome

`context symbol` composes declaration, owner, document, and outline evidence
once within a caller-selected budget.

## Design

- [Bounded context](../../design/search-and-context.md#bounded-context)
- [Agent-facing composition](../../design/agent-integration.md#agent-facing-composition)

## Boundary

The command presents tool evidence and provenance; it does not synthesize
natural-language conclusions. Relationship sections become available with
`MVP-E05` and are not part of this story.

## Acceptance

- Requested declaration, owner, document, and outline sections preserve
  identity, source location, snapshot, resolution, coverage, confidence, and
  provenance.
- Repeated declarations or spans are emitted once and referenced rather than
  duplicated.

## Verification

- Composite fixtures cover declarations, owner variants, documents, outlines,
  partial scope, deduplication, and truncation.

## Dependencies

- `MVP-E04-S05`
- `MVP-E04-S06`
- `MVP-E04-S07`
- `MVP-E04-S09`

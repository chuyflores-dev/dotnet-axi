# MVP-E04-S08 — Compose Symbol Context

## Outcome

`context symbol` composes requested declaration and relationship evidence once
within a caller-selected budget.

## Design

- [Bounded context](../../design/search-and-context.md#bounded-context)
- [Agent-facing composition](../../design/agent-integration.md#agent-facing-composition)

## Boundary

The command presents tool evidence and provenance; it does not synthesize
natural-language conclusions.

## Acceptance

- Requested sections preserve identity, source location, snapshot, resolution,
  coverage, confidence, and provenance.
- Repeated declarations or spans are emitted once and referenced rather than
  duplicated.

## Verification

- Composite fixtures cover declarations, callers, callees, tests, partial
  scope, deduplication, and truncation.

## Dependencies

- `MVP-E04-S05`
- `MVP-E04-S06`
- `MVP-E04-S07`
- `MVP-E04-S09`
- `MVP-E05-S02`
- `MVP-E05-S06`
- `MVP-E05-S07`

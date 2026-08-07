# MVP-E05-S13 — Compose Relationship Context

## Outcome

`context symbol` composes selected semantic relationships into the existing
bounded symbol context without duplicating source evidence.

## Design

- [Bounded context](../../design/search-and-context.md#bounded-context)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)
- [Agent-facing composition](../../design/agent-integration.md#agent-facing-composition)

## Boundary

The command presents selected relationship evidence and provenance. It does
not infer runtime relationships, synthesize conclusions, or include tests
before an affected-test capability ships.

## Acceptance

- Supported reference, implementation, override, derived-type, caller, and
  callee sections reuse the exact relationship contracts and remain explicit
  about applicability, resolution, scope, coverage, confidence, and
  provenance.
- Relationship summaries, source spans, and declarations are emitted once and
  referenced rather than duplicated across sections.
- Section selection, deterministic ordering, truncation, and larger-budget or
  full escape hatches follow the existing context-budget contract.
- Unsupported, inapplicable, partial, and failed relationship sections remain
  distinguishable from verified empty results.

## Verification

- Composite fixtures cover every supported relationship section, mixed
  applicability, partial coverage, shared evidence, deterministic budgeting,
  truncation, and unsupported test inclusion.

## Dependencies

- `MVP-E04-S08`
- `MVP-E05-S02`
- `MVP-E05-S03`
- `MVP-E05-S04`
- `MVP-E05-S05`
- `MVP-E05-S06`
- `MVP-E05-S07`

# MVP-E09-S15 — Teach Semantic Relationships and Graphs in the Agent Skill

## Outcome

The released Agent Skill routes exact relationship, graph, impact, and bounded
relationship-context tasks through the shipped 0.6.0 commands.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)
- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

Guidance does not present partial coverage as complete, convert possible
dispatch into a direct call, claim runtime knowledge, or teach unshipped
analysis, validation, or mutation capabilities.

## Acceptance

- Guidance selects one exact semantic target before relationship traversal and
  teaches entity-ID correction for ambiguous or stale targets.
- References, implementations, overrides, derived types, callers, and callees
  retain their distinct relationship and completeness semantics.
- Project dependencies, cycles, paths, and impact guidance preserves graph
  direction, limits, evidence kinds, and partial coverage.
- Guidance uses impact before important public changes and bounded relationship
  context when it reduces repeated source retrieval.
- The invoked version's help and capabilities remain authoritative, and
  committed, packaged, structured-help, and home-view guidance stay generated
  from one source and byte-consistent where required.

## Verification

- Golden generation and packaged-skill tests cover target correction,
  partial-versus-complete traversal, possible dispatch, graph direction,
  impact uncertainty, bounded relationship context, and absence of later
  analysis or mutation commands.

## Dependencies

- `MVP-E09-S13`
- `MVP-E09-S14`
- `MVP-E05-S01`
- `MVP-E05-S02`
- `MVP-E05-S03`
- `MVP-E05-S04`
- `MVP-E05-S05`
- `MVP-E05-S06`
- `MVP-E05-S07`
- `MVP-E05-S08`
- `MVP-E05-S09`
- `MVP-E05-S10`
- `MVP-E05-S11`
- `MVP-E05-S12`
- `MVP-E05-S13`

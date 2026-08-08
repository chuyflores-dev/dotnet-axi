# MVP-E13-S19 — Add 0.6.0 Semantic-relationship and Graph Tasks

## Outcome

The agent-task corpus adds deterministic semantic-relationship, graph,
impact, and bounded relationship-context scenarios for 0.6.0.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)
- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

The corpus does not add analyzer, SDK execution, validation, package policy,
or mutation tasks before their corresponding capabilities ship.

## Acceptance

- Tasks cover target resolution, references, implementations, overrides,
  derived types, callers, callees, project dependencies, cycles, paths,
  impact, and bounded relationship context.
- Scenarios distinguish partial from complete coverage, verified direct edges
  from possible or heuristic edges, and verified empty results from failed or
  unexpanded scope.
- Each task declares raw-tool and candidate applicability, fixed state,
  deterministic success and safety oracles, timeout, and required evidence.
- Corpus validation rejects ambiguous targets, hidden candidate guidance,
  runtime-completeness claims, unshipped analysis expectations, and
  nondeterministic setup.

## Verification

- Known direct, possible, empty, ambiguous, partial, failed, cyclic, no-path,
  truncated, and unsupported fixtures prove the new task oracles independently
  of a paid agent run.

## Dependencies

- `MVP-E13-S04`
- `MVP-E13-S10`
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
- `MVP-E09-S15`

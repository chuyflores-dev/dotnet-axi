# MVP-E05-S12 — Analyze Impact

## Outcome

`graph impact` summarizes the statically supported change impact of one
project or code entity.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

Candidate tests and convention-based effects remain heuristics with evidence;
impact does not claim complete runtime knowledge.

## Acceptance

- Output summarizes affected projects, documents, candidate tests,
  public-surface impact, important paths, and confidence.
- Limits and partial coverage state what was not expanded.

## Verification

- Impact fixtures cover private and public changes, project dependencies,
  callers, inheritance, tests, depth limits, and broken projects.

## Dependencies

- `MVP-E05-S02`
- `MVP-E05-S03`
- `MVP-E05-S04`
- `MVP-E05-S05`
- `MVP-E05-S06`
- `MVP-E05-S07`
- `MVP-E05-S08`
- `MVP-E05-S09`

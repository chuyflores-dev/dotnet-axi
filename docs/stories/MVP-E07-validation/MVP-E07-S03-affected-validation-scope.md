# MVP-E07-S03 — Select Affected Validation Scope

## Outcome

Validation can map changed paths to the smallest transparent affected project,
dependent, and candidate-test scope.

## Design

- [Validation](../../design/analysis-and-execution.md#validation)
- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

Candidate-test selection remains a labeled heuristic and never implies
complete test coverage.

## Acceptance

- Changed files, owners, dependents, public-surface impact, and configured test
  patterns contribute explicit evidence.
- Unmapped, deleted, unsupported, or ambiguous paths widen or fail scope
  according to declared policy rather than disappearing.

## Verification

- Fixtures cover implementation-only, public API, project-file, test,
  unowned, deleted, and multi-owned changes.

## Dependencies

- `MVP-E02-S04`
- `MVP-E05-S12`
- `MVP-E10-S07`

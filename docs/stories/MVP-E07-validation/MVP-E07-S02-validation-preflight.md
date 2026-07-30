# MVP-E07-S02 — Preflight Validation Effects

## Outcome

A validation profile is fully resolved and discloses executing, network, and
artifact effects before its first check runs.

## Design

- [Validation results and lifecycle](../../design/analysis-and-execution.md#validation-results-and-lifecycle)
- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)

## Boundary

Invalid, unavailable, cyclic, or source-writing checks fail during preflight,
not after partial execution.

## Acceptance

- Named profiles resolve their ordered checks and effect classifications.
- Profile invocation represents consent only for the checks disclosed by the
  resolved plan.

## Verification

- Planner tests cover valid, missing, cyclic, executing, networked,
  artifact-writing, and source-writing configurations.

## Dependencies

- `MVP-E07-S01`
- `MVP-E10-S07`
- `MVP-E11-S01`
- `MVP-E11-S03`

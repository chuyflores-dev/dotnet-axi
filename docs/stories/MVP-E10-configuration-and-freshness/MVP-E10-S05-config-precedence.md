# MVP-E10-S05 — Apply Configuration Precedence

## Outcome

Commands resolve effective values from CLI input, repository configuration,
repository-derived state, and tool defaults in the documented order.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)

## Boundary

Conflicting repeated CLI properties fail; identical repeats may be
deduplicated but never silently reordered.

## Acceptance

- Every effective value retains its source and original representation.
- Workspace selectors, properties, output limits, and configurable defaults
  follow the same precedence mechanism.

## Verification

- Matrix tests cover every source combination, conflicting and duplicate CLI
  values, missing sources, and explicit empty values.

## Dependencies

- `MVP-E10-S03`
- `MVP-E02-S02`

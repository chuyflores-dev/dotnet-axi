# MVP-E10-S07 — Bind Validation Configuration

## Outcome

Validation consumes typed test patterns and acyclic named profiles composed
only of known non-source-writing checks.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)

## Boundary

Configuration cannot invent an unavailable check or place source-writing work
inside a validation profile.

## Acceptance

- Profile references expand deterministically with preserved order and effect
  declarations.
- Test patterns, zero-test policy, check options, and unavailable checks remain
  explicit to the validation planner.

## Verification

- Fixtures cover built-in and custom profiles, nested references, cycles,
  duplicates, unknown checks, source writes, and test policies.

## Dependencies

- `MVP-E10-S04`
- `MVP-E10-S05`
- `MVP-E11-S01`

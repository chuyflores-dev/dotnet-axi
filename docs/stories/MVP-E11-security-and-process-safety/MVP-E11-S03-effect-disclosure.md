# MVP-E11-S03 — Disclose Network and Execution Effects

## Outcome

Help, plans, and results accurately disclose network access, repository-code
execution, sandbox limits, and each write category.

## Design

- [Network and telemetry](../../design/runtime-and-distribution.md#network-and-telemetry)
- [Repository-code execution](../../design/runtime-and-distribution.md#repository-code-execution)

## Boundary

The product never describes an executing operation as read-only merely because
it does not edit C# source.

## Acceptance

- Disclosures are derived from effect classification and preserved in composed
  validation profiles.
- Executing results state that caller operating-system permissions apply unless
  an enforced sandbox is active.

## Verification

- Golden tests cover every operation category, composed profiles, unknown
  escape-hatch effects, and sandbox/no-sandbox states.

## Dependencies

- `MVP-E11-S01`
- `MVP-E01-S08`

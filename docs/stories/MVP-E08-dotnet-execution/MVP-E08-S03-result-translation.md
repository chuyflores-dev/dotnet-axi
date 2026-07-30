# MVP-E08-S03 — Translate Dependency Results

## Outcome

External process results become stable SDK-operation results without parsing
locale-sensitive prose as authoritative structure.

## Design

- [Structured translation](../../design/analysis-and-execution.md#structured-translation)

## Boundary

Raw output may be retained as an artifact but never replaces the normalized
response.

## Acceptance

- Operation, public exit, dependency exit, duration, scope, effects, summary,
  failures, cancellation, timeout, and artifact can be represented.
- Signals and platform-specific codes cannot be confused with CLI usage exit
  `2`.

## Verification

- Translation tests cover success, child failure, unusual exit codes, signals,
  cancellation, timeout, malformed structured logs, and non-English hosts.

## Dependencies

- `MVP-E08-S02`
- `MVP-E01-S03`
- `MVP-E01-S05`

# MVP-E08-S02 — Run Cancellable Child Processes

## Outcome

Adapters can execute one external process through argument-list APIs with
bounded capture, cancellation, timeout, and process-tree termination.

## Design

- [Process and secret safety](../../design/runtime-and-distribution.md#process-and-secret-safety)
- [Structured translation](../../design/analysis-and-execution.md#structured-translation)

## Boundary

This runner never invokes a shell or interpolates user-controlled values into
a command string.

## Acceptance

- Working directory, arguments, environment, output limits, cancellation, and
  timeout are explicit typed inputs.
- Termination covers descendants and preserves the completed and terminated
  lifecycle evidence.

## Verification

- Process fixtures cover stdout/stderr pressure, shell metacharacters, hangs,
  cancellation, timeout, descendants, signals, and cleanup.

## Dependencies

- `MVP-E01-S01`

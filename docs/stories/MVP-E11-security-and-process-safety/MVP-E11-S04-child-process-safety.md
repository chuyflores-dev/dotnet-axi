# MVP-E11-S04 — Harden Child-process Execution

## Outcome

Every child `dotnet`, `rg`, and AST-grep process receives safe argument,
environment, working-directory, capture, cancellation, and termination policy.

## Design

- [Process and secret safety](../../design/runtime-and-distribution.md#process-and-secret-safety)
- [Child safety environment](../../design/analysis-and-execution.md#child-safety-environment)

## Boundary

Repository environment required for correct SDK behavior may pass through, but
the complete environment is never logged or returned.

## Acceptance

- Child defaults disable telemetry, first-run noise, advertising downloads,
  terminal decoration, and certificate generation where supported.
- Shell metacharacters, hostile paths, timeouts, and descendant processes
  cannot escape argument-list execution and process-tree control.

## Verification

- Adversarial process tests inspect arguments/environment and cover hostile
  inputs, hangs, descendants, cancellation, timeout, and platform differences.

## Dependencies

- `MVP-E08-S01`
- `MVP-E08-S02`
- `MVP-E11-S01`

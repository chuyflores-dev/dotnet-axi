# MVP-E08-S07 — Run Tests

## Outcome

`test` invokes official `dotnet test` with stable target, flag,
runner-argument, and result behavior.

## Design

- [Official `dotnet` operations](../../design/analysis-and-execution.md#official-dotnet-operations)
- [Stable MVP flags](../../design/analysis-and-execution.md#stable-mvp-flags)

## Boundary

This story exposes the SDK operation; normalized test-case policy and
validation aggregation belong to `MVP-E07`.

## Acceptance

- Every documented test flag maps without shell interpretation, and runner
  arguments are accepted only after `--`.
- Success, test failure, infrastructure failure, cancellation, timeout, and
  protected logs retain original dependency evidence.

## Verification

- SDK fixtures cover no-restore/no-build modes, filters, loggers, results
  directories, runner arguments, both test platforms, failure, and
  cancellation.

## Dependencies

- `MVP-E08-S01`
- `MVP-E08-S02`
- `MVP-E08-S03`
- `MVP-E08-S04`

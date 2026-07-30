# MVP-E08-S06 — Run Build

## Outcome

`build` invokes official `dotnet build` with stable target, flag, and result
behavior.

## Design

- [Official `dotnet` operations](../../design/analysis-and-execution.md#official-dotnet-operations)
- [Stable MVP flags](../../design/analysis-and-execution.md#stable-mvp-flags)

## Boundary

Build never reimplements MSBuild behavior or infers success from localized
prose.

## Acceptance

- Every documented build flag maps without shell interpretation.
- Successful, failed, cancelled, timed-out, and no-op builds retain dependency
  exit, duration, scope, effects, summary, and protected logs.

## Verification

- SDK fixtures cover success, compilation failure, `--no-restore`, runtime,
  verbosity, no-incremental mode, cancellation, timeout, and invalid input.

## Dependencies

- `MVP-E08-S01`
- `MVP-E08-S02`
- `MVP-E08-S03`
- `MVP-E08-S04`

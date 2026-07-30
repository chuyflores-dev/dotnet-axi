# MVP-E08-S05 — Run Restore

## Outcome

`restore` invokes official `dotnet restore` for the selected target and returns
a structured result.

## Design

- [Official `dotnet` operations](../../design/analysis-and-execution.md#official-dotnet-operations)
- [Stable MVP flags](../../design/analysis-and-execution.md#stable-mvp-flags)

## Boundary

Restore is always executing and may access the network; passive commands never
invoke it implicitly.

## Acceptance

- Target selection and every stable restore flag translate through
  argument-list execution.
- Network, repository-code, artifact, dependency-exit, cancellation, and
  failure evidence are explicit.

## Verification

- SDK fixtures cover no-op, successful, locked, forced, source-selected,
  offline failure, cancellation, and invalid input.

## Dependencies

- `MVP-E08-S01`
- `MVP-E08-S02`
- `MVP-E08-S03`
- `MVP-E08-S04`

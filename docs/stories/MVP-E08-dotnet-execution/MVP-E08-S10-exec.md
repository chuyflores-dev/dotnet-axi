# MVP-E08-S10 — Run Constrained `dotnet` Execution

## Outcome

`exec -- dotnet <arguments>` provides an explicit structured escape hatch to
the selected official `dotnet` host.

## Design

- [Escape hatch](../../design/analysis-and-execution.md#escape-hatch)

## Boundary

`exec` is not a shell runner and rejects any executable other than the selected
official `dotnet`.

## Acceptance

- The first post-separator token and all pass-through arguments are validated
  according to the documented boundary.
- Results disclose unclassified SDK side effects conservatively and preserve
  child exit, cancellation, timeout, and artifacts.

## Verification

- Tests cover valid commands, missing separators, alternate executables, shell
  metacharacters, arbitrary arguments, failure, cancellation, and timeout.

## Dependencies

- `MVP-E08-S01`
- `MVP-E08-S02`
- `MVP-E08-S03`
- `MVP-E08-S04`

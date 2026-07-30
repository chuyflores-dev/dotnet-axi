# MVP-E08-S04 — Validate SDK Arguments

## Outcome

First-class SDK commands validate their stable flags and pass-through
boundaries before starting a child process.

## Design

- [Escape hatch](../../design/analysis-and-execution.md#escape-hatch)
- [Stable MVP flags](../../design/analysis-and-execution.md#stable-mvp-flags)

## Boundary

Unknown input before `--` cannot bypass validation; declared command-specific
pass-through remains explicit.

## Acceptance

- Spelling, arity, defaults, conflicts, repeated flags, targets, and supported
  SDK constraints are enforced.
- Invalid input exits `2` without invoking `dotnet`.

## Verification

- Parser contract tests cover every stable flag, conflict, pass-through
  boundary, unknown input, and missing required value.

## Dependencies

- `MVP-E01-S02`
- `MVP-E08-S01`

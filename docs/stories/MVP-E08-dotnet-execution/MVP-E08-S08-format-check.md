# MVP-E08-S08 — Check Formatting

## Outcome

`format --check` performs non-mutating official format verification and returns
the files or diagnostics that fail policy.

## Design

- [Format safety](../../design/analysis-and-execution.md#format-safety)

## Boundary

Check mode cannot write source and is the only format mode validation may use.

## Acceptance

- The command maps to official verify-no-changes behavior with documented
  target, restore, include, exclude, and severity flags.
- No changes, required changes, child failures, and cancellation remain
  distinct structured results.

## Verification

- Fixtures compare file hashes before and after passing, failing, cancelled,
  and invalid format checks.

## Dependencies

- `MVP-E08-S01`
- `MVP-E08-S02`
- `MVP-E08-S03`
- `MVP-E08-S04`

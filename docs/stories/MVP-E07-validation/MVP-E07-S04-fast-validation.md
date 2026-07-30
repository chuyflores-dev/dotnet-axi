# MVP-E07-S04 — Run Fast Validation

## Outcome

`validate --profile fast` runs the configured non-mutating changed-scope checks
and returns one structured verdict.

## Design

- [Fast profile](../../design/analysis-and-execution.md#fast-profile)

## Boundary

Fast validation never modifies source and does not silently add solution-wide
work.

## Acceptance

- Workspace verification, changed parsing, available affected compilation,
  compiler/analyzer findings, format check, and fast structural rules compose
  in configured order.
- Missing capabilities and partial scope remain explicit in the verdict.

## Verification

- End-to-end fixtures cover pass, diagnostic failure, format failure, missing
  assets, unavailable analyzer, partial scope, and no changes.

## Dependencies

- `MVP-E07-S01`
- `MVP-E07-S02`
- `MVP-E07-S03`
- `MVP-E06-S02`
- `MVP-E06-S03`
- `MVP-E06-S04`
- `MVP-E08-S08`

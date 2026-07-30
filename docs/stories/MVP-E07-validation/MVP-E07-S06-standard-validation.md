# MVP-E07-S06 — Run Standard Validation

## Outcome

`validate --profile standard` runs the configured affected restore, build,
analysis, architecture, format-check, and test workflow.

## Design

- [Standard profile](../../design/analysis-and-execution.md#standard-profile)

## Boundary

Full-solution policy, vulnerability checks, publish checks, and source-writing
operations remain outside the MVP standard profile.

## Acceptance

- Checks execute in resolved dependency order with stable results and retained
  child evidence.
- A failed, skipped, unavailable, partial, or zero-test step affects the final
  verdict according to explicit policy.

## Verification

- End-to-end fixtures cover pass, restore failure, build failure, architecture
  violation, test failure, partial scope, and configured check order.

## Dependencies

- `MVP-E07-S02`
- `MVP-E07-S03`
- `MVP-E07-S05`
- `MVP-E06-S05`
- `MVP-E08-S04`
- `MVP-E08-S05`
- `MVP-E08-S06`
- `MVP-E08-S07`
- `MVP-E08-S08`

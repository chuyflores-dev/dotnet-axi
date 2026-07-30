# MVP-E07-S05 — Normalize Test Results

## Outcome

Validation detects VSTest or Microsoft Testing Platform and maps either runner
to one stable test result model.

## Design

- [Validation results and lifecycle](../../design/analysis-and-execution.md#validation-results-and-lifecycle)

## Boundary

Runner-specific exit codes remain dependency evidence; they do not redefine
the public CLI exit contract.

## Acceptance

- Passed, failed, skipped, errored, cancelled, timed-out, and zero-discovered
  runs remain distinguishable.
- Repository zero-test policy is explicit and zero tests never silently become
  success.

## Verification

- Adapter fixtures cover both platforms, non-English output, result files,
  dependency codes, malformed results, and every zero-test policy.

## Dependencies

- `MVP-E08-S07`
- `MVP-E10-S07`

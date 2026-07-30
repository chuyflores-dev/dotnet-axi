# MVP-E07 — Validation

## Outcome

Agents can run deterministic fast or standard validation and receive a concise
structured completion verdict with retained diagnostic evidence.

## Scope

- Fast and standard profile planning and configuration.
- Ordered analysis, format-check, build, and test checks.
- Affected-scope selection and `--continue-on-error`.
- VSTest and Microsoft Testing Platform normalization.
- Check lifecycle, summaries, cancellation, timeout, artifacts, and exit
  behavior.

## Boundary

Full validation and package/vulnerability policy are post-MVP. Validation
never includes a source-writing check.

## Design

- [Analysis and execution](../../design/analysis-and-execution.md)
- [Runtime and distribution](../../design/runtime-and-distribution.md)
- [CLI and output contract](../../design/output-contract.md)

## Dependencies

- `MVP-E06`
- `MVP-E08`
- `MVP-E10`

## Stories

- [MVP-E07-S01 — Define validation contracts](MVP-E07-S01-validation-contracts.md)
- [MVP-E07-S02 — Preflight validation effects](MVP-E07-S02-validation-preflight.md)
- [MVP-E07-S03 — Select affected validation scope](MVP-E07-S03-affected-validation-scope.md)
- [MVP-E07-S04 — Run fast validation](MVP-E07-S04-fast-validation.md)
- [MVP-E07-S05 — Normalize test results](MVP-E07-S05-test-result-normalization.md)
- [MVP-E07-S06 — Run standard validation](MVP-E07-S06-standard-validation.md)
- [MVP-E07-S07 — Control validation lifecycle](MVP-E07-S07-validation-lifecycle.md)
- [MVP-E07-S08 — Summarize validation evidence](MVP-E07-S08-validation-summary.md)

## Complete when

- Fast and standard profiles disclose side effects before execution and return
  an unambiguous pass, fail, or cancellation verdict.
- Zero tests, skipped checks, partial scope, and child failures cannot silently
  become success.

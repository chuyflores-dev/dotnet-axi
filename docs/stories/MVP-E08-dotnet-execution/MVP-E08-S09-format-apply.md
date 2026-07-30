# MVP-E08-S09 — Apply Formatting

## Outcome

`format --apply` explicitly invokes official format mutation and reports the
source files it changed.

## Design

- [Format safety](../../design/analysis-and-execution.md#format-safety)

## Boundary

Apply is an executing source-writing command and cannot be selected by a
validation profile.

## Acceptance

- Exactly one of check or apply is required before execution.
- Apply reports pre/post file identity, modified paths, child evidence, and
  partial or failed outcomes without claiming unobserved changes.

## Verification

- Mutation fixtures cover successful, no-op, failed, cancelled, scoped, and
  concurrently changed files.

## Dependencies

- `MVP-E08-S08`
- `MVP-E11-S08`

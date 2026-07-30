# MVP-E13-S12 — Produce Release-gate Evidence

## Outcome

One deterministic report evaluates correctness, compatibility, security,
performance, and agent-task results against the MVP release bar.

## Design

- [Performance benchmark](../../design/quality.md#performance-benchmark)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Release bar](../../../REQUIREMENTS.md#release-bar)

## Boundary

A failing or missing gate remains visible and cannot be converted to a release
success by prose or partial evidence.

## Acceptance

- The report evaluates all required gates, identifies its exact artifacts and
  environments, and scopes every agent-experience claim.
- Safety-critical regression, success, token, tool-call, and performance
  calculations match the documented formulas and thresholds.

## Verification

- Golden reports cover passing, failing, missing, incomparable, regressed, and
  partially supported release evidence.

## Dependencies

- `MVP-E12-S08`
- `MVP-E13-S05`
- `MVP-E13-S06`
- `MVP-E13-S07`
- `MVP-E13-S09`
- `MVP-E13-S11`

# MVP-E13-S18 — Correct Codex Benchmark Result Reconciliation

## Outcome

Codex benchmark success and inspected-scope evidence reconcile according to
the declared exact-fact-set semantics without treating search expressions as
repository paths.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Evidence

The retained 0.3.0 series exposed two conservative false failures: correct
object-creation facts arrived in natural line order rather than lexicographic
storage order, and regular-expression query tokens were interpreted as
malformed inspected paths. The candidate also invoked `dnaxi` in 0 of 35 runs,
but the report had no explicit activation outcome.

## Boundary

This story corrects and versions the benchmark protocol. It does not rerun,
smooth, reclassify, or overwrite the retained 0.3.0 series.

## Acceptance

- Exact-fact-set answers are compared as canonical, unique ordinal sets;
  response ordering does not change correctness.
- Inspected-scope extraction ignores glob and regular-expression query tokens
  while still rejecting real paths outside the isolated workspace.
- Normalized adapter results and raw-event reconciliation use the same
  classification and scope rules.
- Reports record candidate `dnaxi` activation and label a zero-activation
  comparison explicitly rather than implying that the product was exercised.
- The retained `MVP-E13-S15` series, hashes, status, and conclusions remain
  immutable.

## Verification

- Live-shaped fixtures cover natural numeric line ordering, glob exclusions,
  escaped regular-expression suffixes, and genuine out-of-workspace paths.
- Normalized and raw-event reconciliation produce the same classifications,
  scope decisions, and activation outcome.

## Dependencies

- `MVP-E13-S15`

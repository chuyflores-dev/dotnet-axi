# MVP-E13-S09 — Measure Cold P95 Performance

## Outcome

The benchmark harness measures documented cold-process operations and computes
nearest-rank P95 on the designated runner.

## Design

- [Performance benchmark](../../design/quality.md#performance-benchmark)

## Boundary

Absolute gates apply only to the declared reference runner; other machines
produce comparative evidence.

## Acceptance

- The harness removes tool state, preserves restored dependencies, performs one
  unmeasured filesystem warm-up, and runs at least 30 measured iterations.
- Home, file/text, syntax, and bounded semantic scenarios record full
  environment and fixture identity.

## Verification

- Harness self-tests validate cold-state preparation, sample counting,
  nearest-rank calculation, timeout handling, and manifest completeness.

## Dependencies

- `MVP-E13-S08`
- `MVP-E01-S07`
- `MVP-E03-S02`
- `MVP-E03-S03`
- `MVP-E03-S08`
- `MVP-E03-S09`
- `MVP-E04-S04`

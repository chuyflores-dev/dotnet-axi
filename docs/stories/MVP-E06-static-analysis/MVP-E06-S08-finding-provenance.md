# MVP-E06-S08 — Merge Finding Provenance

## Outcome

Equivalent findings from multiple engines are linked or merged without hiding
their individual evidence.

## Design

- [Findings](../../design/analysis-and-execution.md#findings)

## Boundary

Similarity alone never upgrades a candidate to verified or discards a
disagreeing engine result.

## Acceptance

- Stable equivalence rules preserve all engine identities, resolutions,
  confidence, locations, and scope.
- Deterministic output distinguishes merged, linked, and independent findings.

## Verification

- Tests cover identical, overlapping, conflicting, candidate, locationless,
  and cross-framework findings.

## Dependencies

- `MVP-E06-S01`
- `MVP-E06-S02`
- `MVP-E06-S03`
- `MVP-E06-S04`
- `MVP-E06-S05`

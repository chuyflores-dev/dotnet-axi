# MVP-E01-S03 — Define Evidence Contracts

## Outcome

Command handlers return typed result, evidence, scope, coverage, confidence,
status, suggestion, and error contracts.

## Design

- [Evidence model](../../design/foundations.md#evidence-model)
- [Evidence envelope](../../design/output-contract.md#evidence-envelope)

## Boundary

The contracts remain serialization-independent and contain no backend-specific
result types.

## Acceptance

- Normal and evidence-bearing results express every required envelope field.
- Invalid combinations such as complete coverage without declared scope are
  rejected by construction or validation.

## Verification

- Contract tests cover valid success, partial, failed, and cancelled results.

## Dependencies

- `MVP-E01-S01`

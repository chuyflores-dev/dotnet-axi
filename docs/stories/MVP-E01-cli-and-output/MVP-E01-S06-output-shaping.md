# MVP-E01-S06 — Shape Bounded Output

## Outcome

Collections and large text are deterministic, field-selectable, and bounded
without hiding omitted evidence.

## Design

- [Schema design](../../design/output-contract.md#schema-design)
- [Evidence envelope](../../design/output-contract.md#evidence-envelope)

## Boundary

Capability-specific ranking remains with the owning command.

## Acceptance

- Shared tie-breaking is locale-independent and stable across repeated runs.
- Limits and truncation report actual included size, known totals, omissions,
  and a concrete retrieval escape hatch.

## Verification

- Golden tests cover ordering, requested fields, unknown fields, limits, and
  truncation.

## Dependencies

- `MVP-E01-S03`
- `MVP-E01-S04`

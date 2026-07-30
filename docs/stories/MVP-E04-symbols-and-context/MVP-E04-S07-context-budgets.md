# MVP-E04-S07 — Enforce Context Budgets

## Outcome

Context builders enforce deterministic character budgets and explain every
truncation.

## Design

- [Bounded context](../../design/search-and-context.md#bounded-context)
- [Schema design](../../design/output-contract.md#schema-design)

## Boundary

This story supplies reusable budgeting and section-selection behavior, not a
specific context composition.

## Acceptance

- Explicit, configured, larger-budget, and full modes are supported.
- Truncated output reports included size, known total, omitted sections,
  approximate token range, and a concrete follow-up command.

## Verification

- Budget tests cover exact boundaries, Unicode, deterministic section order,
  unknown totals, and repeated calls.

## Dependencies

- `MVP-E01-S06`

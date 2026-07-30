# MVP-E10-S03 — Validate Configuration

## Outcome

Invalid configuration fails with the file, property path, and an actionable
correction before affected work begins.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)

## Boundary

Unknown keys, checks, duplicate layer names, cyclic profiles, and invalid
values are errors rather than ignored input.

## Acceptance

- All documented schema and cross-field constraints produce stable usage
  errors and exit `2`.
- Validation collects safe independent errors deterministically without
  executing repository code.

## Verification

- Invalid fixtures cover unknown and misspelled keys, duplicates, cycles,
  ranges, conflicting values, and unsupported schema.

## Dependencies

- `MVP-E10-S02`
- `MVP-E01-S05`

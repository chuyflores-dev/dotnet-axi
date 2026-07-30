# MVP-E10-S08 — Bind Architecture Configuration

## Outcome

Architecture analysis receives validated layer membership and dependency rules
from repository configuration.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)

## Boundary

Configuration declares rules; evaluation and findings remain owned by
`MVP-E06-S05`.

## Acceptance

- Project patterns, layer names, forbidden references, namespace boundaries,
  and public API constraints bind to typed rules.
- Missing, duplicate, cyclic, unmatched, and ambiguous layer definitions fail
  or warn according to explicit schema policy.

## Verification

- Configuration fixtures cover the documented example, overlaps, empty
  matches, cycles, invalid references, and CLI-selected workspace scope.

## Dependencies

- `MVP-E10-S04`
- `MVP-E10-S05`

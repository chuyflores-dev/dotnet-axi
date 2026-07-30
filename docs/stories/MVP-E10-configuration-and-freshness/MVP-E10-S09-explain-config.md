# MVP-E10-S09 — Explain Effective Configuration

## Outcome

`--explain-plan` reports each applicable effective setting and where it came
from.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)
- [Query planning](../../design/output-contract.md#query-planning)

## Boundary

Explanation is passive and redacts secret-bearing values while preserving
enough information to correct configuration.

## Acceptance

- CLI, repository, derived, and default sources remain distinguishable.
- Fixed scope values propagate into the reported plan and suggested follow-up
  commands.

## Verification

- Golden tests cover all precedence sources, overrides, redaction, unavailable
  capabilities, and invalid preflight.

## Dependencies

- `MVP-E10-S05`
- `MVP-E01-S09`
- `MVP-E11-S05`

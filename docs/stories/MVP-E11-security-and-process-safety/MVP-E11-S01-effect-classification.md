# MVP-E11-S01 — Classify Operation Effects

## Outcome

Every command and validation check declares whether it is passive or executing
and which network, repository-code, artifact, metadata, user-state, and source
writes it may perform.

## Design

- [Passive and executing operations](../../design/foundations.md#passive-and-executing-operations)
- [Repository-code execution](../../design/runtime-and-distribution.md#repository-code-execution)

## Boundary

Classification is typed policy data, not a help-text convention.

## Acceptance

- All documented effect categories can be represented and queried before
  execution.
- An unclassified operation cannot enter the command or validation registry.

## Verification

- Registry tests enumerate every command/check and fail for missing,
  contradictory, or source-writing validation classifications.

## Dependencies

- `MVP-E01-S03`

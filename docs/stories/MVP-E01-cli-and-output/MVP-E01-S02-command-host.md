# MVP-E01-S02 — Create the Command Host

## Outcome

The CLI parses arguments and dispatches a command through a replaceable handler
without interactive input.

## Design

- [System architecture](../../design/foundations.md#system-architecture)
- [Errors and output channels](../../design/output-contract.md#errors-and-output-channels)

## Boundary

This story provides dispatch and parser infrastructure, not capability command
implementations.

## Acceptance

- Root and nested command handlers receive typed arguments.
- Unknown commands and flags are rejected before a handler or dependency runs.

## Verification

- Parser tests cover dispatch, unknown input, and noninteractive behavior.

## Dependencies

- `MVP-E01-S01`

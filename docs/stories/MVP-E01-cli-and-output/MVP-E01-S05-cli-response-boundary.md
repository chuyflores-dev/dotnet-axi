# MVP-E01-S05 — Enforce the CLI Response Boundary

## Outcome

Every command uses structured stdout, diagnostic stderr, and the public
`0/1/2` exit contract consistently.

## Design

- [Errors and output channels](../../design/output-contract.md#errors-and-output-channels)

## Boundary

Dependency exit codes remain result data and never become new public CLI exit
codes.

## Acceptance

- Success, empty, partial, failed, cancelled, and usage results map correctly.
- Progress, debug output, raw diagnostics, and stack traces never enter normal
  stdout.

## Verification

- Process-level tests assert stdout, stderr, and exit code for each result
  class.

## Dependencies

- `MVP-E01-S02`
- `MVP-E01-S04`

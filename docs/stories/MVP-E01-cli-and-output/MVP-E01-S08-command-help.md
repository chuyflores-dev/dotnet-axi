# MVP-E01-S08 — Generate Command Help

## Outcome

Root and subcommand help describe the implemented CLI through structured
output.

## Design

- [Home, help, and suggestions](../../design/output-contract.md#home-help-and-suggestions)

## Boundary

Help is passive, describes only implemented behavior, and does not probe
repository capabilities.

## Acceptance

- Every subcommand reports its required arguments, flags, defaults,
  classification, and two or three representative examples.
- Unknown or unavailable commands are not advertised as implemented.

## Verification

- Golden CLI tests cover root help, every registered subcommand, inherited
  flags, examples, and passive behavior.

## Dependencies

- `MVP-E01-S02`
- `MVP-E01-S04`

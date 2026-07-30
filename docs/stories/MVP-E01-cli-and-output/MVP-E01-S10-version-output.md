# MVP-E01-S10 — Report the CLI Version

## Outcome

`-v` and `--version` return the installed tool and output-schema versions
through structured output.

## Design

- [Home, help, and suggestions](../../design/output-contract.md#home-help-and-suggestions)
- [Schema evolution](../../design/output-contract.md#schema-evolution)

## Boundary

Runtime dependency capability probing belongs to `MVP-E12-S03`.

## Acceptance

- Global `-v` means version only before subcommand dispatch.
- Version output is passive, deterministic, and identifies
  `dotnet-axi/v1`.

## Verification

- Process tests cover `-v`, `--version`, subcommand verbosity boundaries, and
  package-version substitution.

## Dependencies

- `MVP-E01-S02`
- `MVP-E01-S04`

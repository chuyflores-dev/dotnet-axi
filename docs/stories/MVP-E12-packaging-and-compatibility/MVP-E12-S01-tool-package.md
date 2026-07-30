# MVP-E12-S01 — Package the .NET Tool

## Outcome

The release can produce a versioned .NET global/local tool package with pinned
build-time dependencies and the configured public command.

## Design

- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)
- [Compatibility baseline](../../design/runtime-and-distribution.md#compatibility-baseline)

## Boundary

This story creates the package artifact; publishing to an external registry is
a separate release action.

## Acceptance

- Package metadata declares `Apache-2.0`; the entry point, target framework,
  dependency pins, symbols, and reproducible build inputs are explicit.
- Installing the package does not require Git, `rg`, AST-grep, a daemon, or a
  repository index.

## Verification

- Pack verification inspects package contents and installs it into an isolated
  local tool store.

## Dependencies

- `MVP-E01-S01`
- `MVP-E01-S02`
- `MVP-E01-S04`

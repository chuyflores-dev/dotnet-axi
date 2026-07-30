# MVP-E10-S02 — Parse Configuration Schema v1

## Outcome

`dotnet-axi.yml` version 1 parses into typed workspace, search, structural,
validation, architecture, output, and performance settings.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)

## Boundary

Parsing does not apply CLI precedence or execute configured checks.

## Acceptance

- The required integer version and every documented setting preserve source
  location and user intent.
- Unsupported versions return a structured migration requirement.

## Verification

- Parser fixtures cover the documented example, minimal configuration, every
  section, YAML type errors, and unsupported versions.

## Dependencies

- `MVP-E10-S01`

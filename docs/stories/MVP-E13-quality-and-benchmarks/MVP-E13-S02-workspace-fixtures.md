# MVP-E13-S02 — Add Workspace and Project Fixtures

## Outcome

The fixture catalog represents supported solution, project, language,
framework, analyzer, generator, and test-runner shapes.

## Design

- [Integration fixtures](../../design/quality.md#integration-fixtures)

## Boundary

Fixtures are minimal reproductions and do not duplicate command-specific
expected outputs.

## Acceptance

- Single/multi-project, `.sln`/`.slnx`, multi-targeting, linked files,
  conditional compilation, generated code, cycles, analyzers, generators,
  VSTest, and MTP are available.
- Each fixture records its intended capabilities and selected SDK requirements.

## Verification

- A catalog test builds or intentionally fails every fixture according to its
  manifest.

## Dependencies

- `MVP-E13-S01`

# MVP-E06-S04 — Run Structural Rules

## Outcome

`analyze structural` runs configured tool-owned Roslyn syntax rules and returns
normalized syntax-policy findings.

## Design

- [Structural and architecture rules](../../design/analysis-and-execution.md#structural-and-architecture-rules)

## Boundary

Structural policies remain syntax evidence unless a separate verifier is
declared and used.

## Acceptance

- Rule discovery, selected rule scope, severity, location, cancellation, and
  unknown-rule failures are explicit.
- Empty catches, direct construction, forbidden syntax, and migration-pattern
  fixtures can be expressed without source rewrites.

## Verification

- Rule fixtures cover configured IDs and options, valid and invalid
  configuration, matches, no matches, malformed syntax, and generated-code
  scope.

## Dependencies

- `MVP-E06-S01`
- `MVP-E03-S08`
- `MVP-E03-S09`
- `MVP-E03-S10`
- `MVP-E03-S11`
- `MVP-E03-S12`
- `MVP-E10-S06`

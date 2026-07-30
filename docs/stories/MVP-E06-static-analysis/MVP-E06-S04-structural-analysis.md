# MVP-E06-S04 — Run Structural Rules

## Outcome

`analyze structural` runs configured AST-grep YAML rules and returns normalized
syntax-policy findings.

## Design

- [Structural and architecture rules](../../design/analysis-and-execution.md#structural-and-architecture-rules)

## Boundary

Structural policies remain syntax evidence unless a separate verifier is
declared and used.

## Acceptance

- Rule discovery, selected rule scope, severity, location, cancellation, and
  optional-engine failures are explicit.
- Empty catches, direct construction, forbidden syntax, and migration-pattern
  fixtures can be expressed without source rewrites.

## Verification

- Rule fixtures cover configured directories, valid and invalid YAML, matches,
  no matches, missing AST-grep, and generated-code scope.

## Dependencies

- `MVP-E06-S01`
- `MVP-E03-S07`
- `MVP-E10-S06`

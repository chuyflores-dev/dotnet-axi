# MVP-E06-S01 — Define Finding Contracts

## Outcome

All analysis engines return one typed finding model with stable identity,
severity, location, resolution, confidence, scope, and provenance.

## Design

- [Findings](../../design/analysis-and-execution.md#findings)

## Boundary

Engine-specific diagnostics are translated at adapter boundaries and never
become the public schema.

## Acceptance

- Compiler, analyzer, structural, architecture, and heuristic findings can be
  represented without losing their source meaning.
- Candidate findings cannot be constructed as verified semantic facts.

## Verification

- Contract tests cover every engine, severity, location state, confidence, and
  partial-coverage combination.

## Dependencies

- `MVP-E01-S03`

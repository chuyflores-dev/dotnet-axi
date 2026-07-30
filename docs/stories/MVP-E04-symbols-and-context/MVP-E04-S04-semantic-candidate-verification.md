# MVP-E04-S04 — Verify Syntax Candidates Semantically

## Outcome

Structural candidates can be verified as a declared compiler construct in the
smallest valid project and framework scope.

## Design

- [Semantic verification](../../design/search-and-context.md#semantic-verification)
- [Level 2 — Candidate-scoped semantics](../../design/foundations.md#level-2--candidate-scoped-semantics)

## Boundary

Bare `--verify` requires rule metadata; arbitrary syntax nodes cannot invent a
compiler meaning.

## Acceptance

- `--verify-as` and metadata-driven `--verify` report discovered, verified,
  rejected, and unresolved counts.
- Every owning project/framework variant and all partial-coverage reasons stay
  explicit.

## Verification

- Roslyn oracle fixtures cover each verifier kind, multi-ownership,
  ambiguity, broken projects, and missing metadata.

## Dependencies

- `MVP-E03-S07`
- `MVP-E03-S08`
- `MVP-E02-S05`
- `MVP-E02-S06`

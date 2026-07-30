# MVP-E13-S04 — Build Structural and Roslyn Oracles

## Outcome

Structural candidates and semantic relationships can be compared with direct
AST-grep and Roslyn authority in repeatable tests.

## Design

- [Structural and Roslyn oracles](../../design/quality.md#structural-and-roslyn-oracles)

## Boundary

Oracles test adapter truth and coordinate translation; they do not reuse the
production adapter implementation being verified.

## Acceptance

- Candidate, verification, location, ignore, no-match, reference, inheritance,
  call, overload, linked-file, and framework results are comparable.
- Differences report exact fixture, authority result, product result, and
  coverage context.

## Verification

- Oracle self-tests include known matching and intentionally divergent
  candidate-versus-semantic cases.

## Dependencies

- `MVP-E13-S02`
- `MVP-E03-S07`
- `MVP-E04-S04`
- `MVP-E05-S02`
- `MVP-E05-S03`
- `MVP-E05-S04`
- `MVP-E05-S05`
- `MVP-E05-S06`
- `MVP-E05-S07`

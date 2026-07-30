# MVP-E03-S02 — Search Files

## Outcome

`search file` returns deterministic path matches and owning-project summaries
without requiring compilation.

## Design

- [File search](../../design/search-and-context.md#file-search)

## Boundary

File contents and compiler meaning are outside this command.

## Acceptance

- Default matching and every documented scope, case, extension, glob,
  generated, changed, and limit option work.
- Multi-owned paths appear once and no-match results succeed explicitly.

## Verification

- Command tests cover ranking, flags, ownership, limiting, and empty results.

## Dependencies

- `MVP-E03-S01`
- `MVP-E01-S06`
- `MVP-E02-S04`

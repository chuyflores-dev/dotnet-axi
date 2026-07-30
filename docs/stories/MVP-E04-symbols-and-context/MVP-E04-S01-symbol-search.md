# MVP-E04-S01 — Search Symbol Declarations

## Outcome

`search symbol` discovers and ranks declaration candidates before loading
semantic projects.

## Design

- [Symbol declarations](../../design/search-and-context.md#symbol-declarations)

## Boundary

Search identifies declarations and ownership candidates; exact relationship
queries resolve a single semantic target later.

## Acceptance

- Ranking and kind, namespace, project, path, accessibility, test, and
  generated-code filters follow the design.
- Default rows remain compact and additional fields are opt-in.

## Verification

- Declaration fixtures cover ranking tiers, overloads, partial types, linked
  files, filters, and deterministic ties.

## Dependencies

- `MVP-E02-S06`
- `MVP-E03-S01`
- `MVP-E01-S06`

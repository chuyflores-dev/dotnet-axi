# MVP-E04-S06 — Show a Document

## Outcome

`show document` returns a bounded preview with document identity and an outline
reference instead of dumping an entire source file.

## Design

- [Show and outline](../../design/search-and-context.md#show-and-outline)

## Boundary

Full source remains available only through an explicit larger-budget or full
request.

## Acceptance

- Output identifies the normalized path, ownership, snapshot, encoding,
  included size, known total, and truncation escape hatch.
- Default output remains bounded and respects generated-code and external-path
  policy.

## Verification

- Fixtures cover small, large, encoded, generated, external, missing, changed,
  and malformed documents.

## Dependencies

- `MVP-E02-S07`
- `MVP-E01-S06`

# MVP-E03-S03 — Search Literal Text

## Outcome

`search text` finds literal text with stable case, encoding, preview, count,
and skipped-file behavior.

## Design

- [Text search](../../design/search-and-context.md#text-search)
- [Encoding and case](../../design/search-and-context.md#encoding-and-case)

## Boundary

The built-in engine is authoritative; acceleration belongs to
`MVP-E03-S05`.

## Acceptance

- Ordinal case modes, UTF-8, UTF-8 BOM, UTF-16, binary detection, limits, and
  scope flags follow the design.
- Undecodable files are counted and do not fail unrelated matches.

## Verification

- Text fixtures cover encodings, locale-sensitive characters, binary data,
  previews, unknown totals, and empty results.

## Dependencies

- `MVP-E03-S01`
- `MVP-E01-S06`
- `MVP-E02-S04`

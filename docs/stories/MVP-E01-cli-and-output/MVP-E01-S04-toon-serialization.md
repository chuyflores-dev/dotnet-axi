# MVP-E01-S04 — Serialize TOON Output

## Outcome

Typed CLI results serialize as UTF-8, LF-only TOON v4.1 documents under
`dotnet-axi/v1`.

## Design

- [TOON encoding](../../design/output-contract.md#toon-encoding)
- [Schema evolution](../../design/output-contract.md#schema-evolution)

## Boundary

Only the CLI output layer depends on the selected TOON implementation.

## Acceptance

- Every emitted document starts with the schema and canonical command.
- Untrusted text, array lengths, row widths, encoding, and line endings obey
  the pinned format.

## Verification

- Representative documents strict-decode with the pinned TOON corpus.

## Dependencies

- `MVP-E01-S03`

# MVP-E13-S05 — Verify TOON Conformance

## Outcome

Every stdout document strict-decodes with the pinned TOON v4.1 corpus under
`dotnet-axi/v1`.

## Design

- [Output and platform](../../design/quality.md#output-and-platform)
- [TOON encoding](../../design/output-contract.md#toon-encoding)

## Boundary

Conformance verifies the wire format and schema invariants, not
capability-specific semantic correctness.

## Acceptance

- Golden output covers all result classes and prevents accidental default
  schema bloat.
- Fuzzed untrusted strings preserve encoding, escaping, declared array lengths,
  row widths, UTF-8, and LF-only output.

## Verification

- The pinned encoder corpus, golden documents, and fuzz suite run against the
  production serializer.

## Dependencies

- `MVP-E01-S04`
- `MVP-E01-S05`
- `MVP-E01-S06`

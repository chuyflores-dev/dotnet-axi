# MVP-E11-S05 — Protect Structured Output and Secrets

## Outcome

Repository and dependency text cannot escape TOON structure, inject fields, or
expose recognized credentials and secret-bearing arguments.

## Design

- [Process and secret safety](../../design/runtime-and-distribution.md#process-and-secret-safety)
- [TOON encoding](../../design/output-contract.md#toon-encoding)

## Boundary

Redaction is defense in depth and does not claim arbitrary raw repository logs
are secret-free.

## Acceptance

- All untrusted strings pass through the serializer and known secret classes
  are redacted in structured output, stderr, and metadata.
- Complete environments, tokens, authorization headers, and secret-bearing
  arguments are never emitted.

## Verification

- Fuzz and adversarial tests cover control characters, TOON injection, paths,
  diagnostics, test names, environment variables, headers, tokens, and false
  positives.

## Dependencies

- `MVP-E01-S04`
- `MVP-E08-S03`

# MVP-E03-S09 — Search Invocation Syntax

## Outcome

`search syntax invocation --name <name>` returns stable invocation candidates.

## Design

- [Stable syntax queries](../../design/search-and-context.md#stable-syntax-queries)

## Boundary

Results are syntax candidates until an explicit semantic verifier resolves the
invoked symbol.

## Acceptance

- Simple, member-access, conditional-access, generic, and malformed invocation
  shapes follow documented name and scope matching.
- Roslyn fallback and supported AST-grep execution produce equivalent
  normalized candidates.

## Verification

- Paired-engine fixtures cover invocation shapes, name filters, false
  candidates, generated-code scope, and empty results.

## Dependencies

- `MVP-E03-S06`
- `MVP-E03-S08`

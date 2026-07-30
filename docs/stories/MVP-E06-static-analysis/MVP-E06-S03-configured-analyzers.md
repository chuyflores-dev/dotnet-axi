# MVP-E06-S03 — Run Configured Analyzers

## Outcome

`analyze analyzers` executes only analyzers already configured by the selected
repository and returns normalized findings.

## Design

- [Configured analyzers](../../design/analysis-and-execution.md#configured-analyzers)
- [Passive and executing operations](../../design/foundations.md#passive-and-executing-operations)

## Boundary

Additional analyzer packs are not installed implicitly, and passive semantic
commands never execute configured analyzers.

## Acceptance

- The command is classified as executing, honors selected scope, cancellation,
  and timeout, and reports analyzer/generator identities when available.
- Generated-source requirements and omitted execution produce explicit
  coverage rather than authoritative passive results.

## Verification

- Analyzer fixtures cover configured diagnostics, generators, consent,
  cancellation, timeout, and passive-command isolation.

## Dependencies

- `MVP-E06-S01`
- `MVP-E02-S06`
- `MVP-E11-S01`

# MVP-E12-S07 — Isolate Incompatible Compiler Hosts

## Outcome

An incompatible selected SDK/MSBuild/Roslyn combination uses a compatible
isolated helper or returns a structured compatibility error.

## Design

- [Compatibility baseline](../../design/runtime-and-distribution.md#compatibility-baseline)

## Boundary

The tool never continues in-process with mismatched assemblies while claiming
authoritative semantic or MSBuild results.

## Acceptance

- Compatibility is checked before project evaluation or Roslyn loading.
- Helper selection preserves the same typed internal and output contracts and
  reports its exact runtime identity.

## Verification

- Controlled mismatch fixtures cover helper success, missing helper,
  unsupported SDK, corrupt protocol, cancellation, and no false complete
  result.

## Dependencies

- `MVP-E08-S01`
- `MVP-E12-S06`
- `MVP-E08-S02`

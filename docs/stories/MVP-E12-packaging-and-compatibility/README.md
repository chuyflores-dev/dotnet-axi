# MVP-E12 — Packaging and Compatibility

## Outcome

The CLI installs and runs predictably as a .NET tool across the published
platform, SDK, and optional-engine compatibility matrix.

## Scope

- Global and local .NET tool packaging and invocation.
- Version and capability reporting.
- Supported OS/RID and .NET SDK 8, 9, and 10 validation.
- Roslyn/MSBuild host compatibility and isolated failure behavior.
- Git, `rg`, and AST-grep discovery, version reporting, and graceful
  degradation.

## Boundary

Self-update behavior, direct Tree-sitter bindings, and mandatory optional
accelerators are outside the MVP.

## Design

- [Runtime and distribution](../../design/runtime-and-distribution.md)
- [Design foundations](../../design/foundations.md)
- [Quality](../../design/quality.md)

## Dependencies

- `MVP-E01`
- `MVP-E02`
- `MVP-E03`
- `MVP-E08`

## Stories

- [MVP-E12-S01 — Package the .NET tool](MVP-E12-S01-tool-package.md)
- [MVP-E12-S02 — Verify global and local invocation](MVP-E12-S02-tool-invocation.md)
- [MVP-E12-S03 — Report versions and capabilities](MVP-E12-S03-version-and-capabilities.md)
- [MVP-E12-S04 — Degrade optional dependencies safely](MVP-E12-S04-optional-dependencies.md)
- [MVP-E12-S05 — Publish the platform matrix](MVP-E12-S05-platform-matrix.md)
- [MVP-E12-S06 — Verify the SDK matrix](MVP-E12-S06-sdk-matrix.md)
- [MVP-E12-S07 — Isolate incompatible compiler hosts](MVP-E12-S07-host-isolation.md)
- [MVP-E12-S08 — Publish compatibility evidence](MVP-E12-S08-compatibility-evidence.md)

## Complete when

- Published packages expose the same contract for supported global and local
  invocation.
- Every tested or unsupported SDK, platform, and optional dependency state is
  reported accurately and never produces false authoritative results.

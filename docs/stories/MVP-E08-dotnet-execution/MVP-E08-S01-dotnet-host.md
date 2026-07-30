# MVP-E08-S01 — Resolve the `dotnet` Host

## Outcome

SDK operations resolve one supported official `dotnet` executable and its
selected SDK/MSBuild context.

## Design

- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)
- [Compatibility baseline](../../design/runtime-and-distribution.md#compatibility-baseline)

## Boundary

The tool respects repository `global.json` and does not silently continue with
an incompatible in-process MSBuild/Roslyn host.

## Acceptance

- PATH and explicit supported host selection report the executable, SDK, and
  compatibility state.
- Missing, unsupported, and mismatched hosts fail with an actionable
  structured correction.

## Verification

- Host fixtures cover PATH selection, explicit paths, `global.json`,
  unavailable SDKs, prerelease/roll-forward policy, and mismatches.

## Dependencies

- `MVP-E02-S02`
- `MVP-E02-S06`

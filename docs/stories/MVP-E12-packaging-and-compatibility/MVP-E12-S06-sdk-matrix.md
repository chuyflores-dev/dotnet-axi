# MVP-E12-S06 — Verify the SDK Matrix

## Outcome

Passive semantic and SDK contracts are exercised against supported installed
.NET 8, .NET 9, and .NET 10 feature bands.

## Design

- [Compatibility baseline](../../design/runtime-and-distribution.md#compatibility-baseline)

## Boundary

Newer untested SDKs are labeled unverified and cannot silently inherit a
tested authoritative claim.

## Acceptance

- SDK selection respects `global.json`, roll-forward, prerelease, framework,
  and MSBuild property context on each supported feature band.
- Semantic and SDK operation results identify the exact versions exercised.

## Verification

- A version matrix runs representative workspace, semantic, restore, build,
  test, and format scenarios for each declared feature band.

## Dependencies

- `MVP-E12-S02`
- `MVP-E02-S06`
- `MVP-E05-S02`
- `MVP-E08-S05`
- `MVP-E08-S06`
- `MVP-E08-S07`
- `MVP-E08-S08`

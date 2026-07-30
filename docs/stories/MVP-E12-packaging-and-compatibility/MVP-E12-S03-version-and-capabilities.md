# MVP-E12-S03 — Report Versions and Capabilities

## Outcome

Home and version output report tool, schema, SDK, Roslyn/MSBuild, Git, and
optional-engine compatibility state.

## Design

- [Required and optional dependencies](../../design/runtime-and-distribution.md#required-and-optional-dependencies)
- [Compatibility baseline](../../design/runtime-and-distribution.md#compatibility-baseline)

## Boundary

Capability detection is passive and does not install, update, restore, or
download dependencies.

## Acceptance

- Present, missing, supported, unsupported, and unverified versions remain
  distinct.
- Capability data identifies the selected host and degradation available to
  the requested command.

## Verification

- Golden tests use controlled version probes for every supported and degraded
  state.

## Dependencies

- `MVP-E12-S02`
- `MVP-E08-S01`
- `MVP-E01-S07`
- `MVP-E01-S10`

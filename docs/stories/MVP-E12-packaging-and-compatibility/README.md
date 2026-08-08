# MVP-E12 — Packaging and Compatibility

## Outcome

The CLI installs and runs predictably as a .NET tool across the published
platform, SDK, and optional-engine compatibility matrix.

## Scope

- Global and local .NET tool packaging and invocation.
- Version and capability reporting.
- Tag-derived release versions, candidate verification, and release-driven
  trusted publishing.
- Supported OS/RID and .NET SDK 8, 9, and 10 validation.
- Roslyn/MSBuild host compatibility and isolated failure behavior.
- Git and `rg` discovery, version reporting, and graceful degradation.
- Constrained-host package behavior across supported platforms and invocation forms.

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
- [MVP-E12-S09 — Plan release versions and publishing](MVP-E12-S09-release-version-plan.md)
- [MVP-E12-S10 — Verify constrained agent hosts](MVP-E12-S10-constrained-agent-hosts.md)
- [MVP-E12-S11 — Derive versions from release tags](MVP-E12-S11-minver-versioning.md)
- [MVP-E12-S12 — Produce release-candidate artifacts](MVP-E12-S12-release-candidate-artifacts.md)
- [MVP-E12-S13 — Protect release-tag creation](MVP-E12-S13-protected-release-tag.md)
- [MVP-E12-S14 — Publish from a GitHub Release](MVP-E12-S14-protected-publication.md)
- [MVP-E12-S15 — Configure trusted NuGet publishing](MVP-E12-S15-trusted-publishing.md)
- [MVP-E12-S16 — Prepare the 0.2.0 release candidate](MVP-E12-S16-release-candidate.md)
- [MVP-E12-S17 — Publish and verify 0.2.0](MVP-E12-S17-publish-0.2.0.md)
- [MVP-E12-S18 — Prepare the 0.3.0 release candidate](MVP-E12-S18-prepare-0.3.0-release-candidate.md)
- [MVP-E12-S19 — Publish and verify 0.3.0](MVP-E12-S19-publish-0.3.0.md)
- [MVP-E12-S20 — Prepare the 0.4.0 release candidate](MVP-E12-S20-prepare-0.4.0-release-candidate.md)
- [MVP-E12-S21 — Publish and verify 0.4.0](MVP-E12-S21-publish-0.4.0.md)
- [MVP-E12-S22 — Prepare the 0.5.0 release candidate](MVP-E12-S22-prepare-0.5.0-release-candidate.md)
- [MVP-E12-S23 — Publish and verify 0.5.0](MVP-E12-S23-publish-0.5.0.md)
- [MVP-E12-S24 — Prepare the 0.6.0 release candidate](MVP-E12-S24-prepare-0.6.0-release-candidate.md)
- [MVP-E12-S25 — Publish and verify 0.6.0](MVP-E12-S25-publish-0.6.0.md)
- [MVP-E12-S26 — Prepare the 0.7.0 release candidate](MVP-E12-S26-prepare-0.7.0-release-candidate.md)
- [MVP-E12-S27 — Publish and verify 0.7.0](MVP-E12-S27-publish-0.7.0.md)
- [MVP-E12-S28 — Prepare the 0.8.0 release candidate](MVP-E12-S28-prepare-0.8.0-release-candidate.md)
- [MVP-E12-S29 — Publish and verify 0.8.0](MVP-E12-S29-publish-0.8.0.md)
- [MVP-E12-S30 — Prepare the 0.9.0 release candidate](MVP-E12-S30-prepare-0.9.0-release-candidate.md)
- [MVP-E12-S31 — Publish and verify 0.9.0](MVP-E12-S31-publish-0.9.0.md)
- [MVP-E12-S32 — Prepare the 1.0.0 release candidate](MVP-E12-S32-prepare-1.0.0-release-candidate.md)
- [MVP-E12-S33 — Publish and verify 1.0.0](MVP-E12-S33-publish-1.0.0.md)
- [MVP-E12-S34 — Make dnx the canonical package invocation](MVP-E12-S34-dnx-first-package-identity.md)
- [MVP-E12-S35 — Prepare the 0.10.0 release candidate](MVP-E12-S35-prepare-0.10.0-release-candidate.md)
- [MVP-E12-S36 — Publish and verify 0.10.0](MVP-E12-S36-publish-0.10.0.md)

## Complete when

- Published packages expose the same contract for supported global and local
  invocation.
- Every tested or unsupported SDK, platform, and optional dependency state is
  reported accurately and never produces false authoritative results.

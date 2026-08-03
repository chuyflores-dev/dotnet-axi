# MVP-E12-S14 — Protect External Publication

## Outcome

A manually approved workflow publishes one already-tagged, verified package
and records the resulting release evidence.

## Design

- [Verification and publishing boundary](../../design/releases.md#verification-and-publishing-boundary)
- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

Pull-request and `main` CI remain unable to publish. Implementing and dry-run
testing the workflow does not authorize a NuGet push or GitHub Release.

## Acceptance

- Manual input selects an existing `v<version>` tag whose commit belongs to
  `main`; arbitrary commits and untagged versions are rejected.
- Tests, packaging, package inspection, installation, and invocation complete
  before the protected publishing job can start.
- The protected job publishes the verified package and symbols only when the
  version is absent from NuGet, then verifies public global and `dnx`
  invocation before publishing the matching GitHub Release.
- Permissions, credentials, artifacts, and concurrency are scoped to the
  smallest job and release identity that need them.

## Verification

- A dry run exercises every pre-publication gate and retains evidence without
  requesting credentials, pushing a package, or creating a GitHub Release.
- Negative cases cover tag disagreement, non-main commits, duplicate package
  versions, failed checks, and missing evidence.

## Dependencies

- `MVP-E12-S01`
- `MVP-E12-S02`
- `MVP-E12-S11`
- `MVP-E12-S12`

# MVP-E12-S12 — Produce Release-candidate Artifacts

## Outcome

A manually requested, non-publishing workflow produces and verifies the exact
package proposed for a release.

## Design

- [Verification and publishing boundary](../../design/releases.md#verification-and-publishing-boundary)
- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)

## Boundary

Candidate artifacts are disposable verification evidence. They are not tags,
NuGet publications, GitHub Releases, or full platform-support claims.

## Acceptance

- The workflow accepts an exact commit and candidate version, uses read-only
  repository permissions, and receives no publishing credential.
- Canonical tests and the complete local package verifier pass before the
  candidate package and symbols are retained with checksums.
- Lightweight Windows, macOS, and Linux jobs install the candidate and verify
  global, local, and `dnx` version parity.
- Candidate evidence identifies the commit, requested and observed versions,
  SDK, OS, RID, package files, and checksums.

## Verification

- A dry run for `0.2.0` succeeds without changing GitHub, Git, or NuGet state.
- Invalid versions, missing artifacts, version disagreement, and failed smoke
  jobs prevent candidate success.

## Dependencies

- `MVP-E12-S01`
- `MVP-E12-S02`
- `MVP-E12-S11`

# MVP-E12-S16 — Prepare the 0.2.0 Release Candidate

## Outcome

One reviewed commit is ready to become `v0.2.0`, with final user-facing
instructions and passing release-candidate evidence.

## Design

- [0.2.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

The candidate pull request remains unmerged until release authorization. This
story does not create `v0.2.0`, publish to NuGet, or create a GitHub Release.

## Acceptance

- User-facing installation and `dnx` examples name version `0.2.0` and describe
  only the capability and compatibility actually verified for this milestone.
- Release notes identify the included milestone outcomes and known limitations
  without making benchmark or full platform-matrix claims.
- Candidate artifacts, checksums, package metadata, installation smoke tests,
  canonical checks, and CI pass for the proposed commit.
- The package ID is still available immediately before release authorization.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no release-side state
  exists.

## Dependencies

- `MVP-E12-S11`
- `MVP-E12-S12`
- `MVP-E12-S13`
- `MVP-E12-S14`
- `MVP-E12-S15`

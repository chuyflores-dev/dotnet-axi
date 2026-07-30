# MVP-E12-S09 — Plan Release Versions and Publishing

## Outcome

Product versions `0.1.0` through `1.0.0` have cumulative capability gates, and
external package publication is an explicit release action.

## Design

- [Releases and versioning](../../design/releases.md)
- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)

## Boundary

This story defines release policy and GitHub milestones. Package creation
belongs to `MVP-E12-S01`; release automation and the first external publish are
separate implementation and release actions.

## Acceptance

- Stable targets from `0.1.0` through `1.0.0` have coherent cumulative
  outcomes.
- Prerelease, minor, patch, tag, and version-source rules are explicit.
- Pull-request and `main` automation cannot publish to NuGet.
- GitHub milestones track version scope without introducing invented dates.

## Verification

- Review the design against the existing epic outcomes and dependencies.
- Confirm GitHub contains one open undated milestone for every planned stable
  target.

## Dependencies

- `MVP-E01-S10`

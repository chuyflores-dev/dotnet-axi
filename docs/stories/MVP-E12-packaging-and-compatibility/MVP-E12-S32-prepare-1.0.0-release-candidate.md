# MVP-E12-S32 — Prepare the 1.0.0 Release Candidate

## Outcome

One reviewed commit is ready to become the supported `v1.0.0` MVP release,
with a passing final certification and no known release blocker.

## Design

- [1.0.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)
- [Release bar](../../../REQUIREMENTS.md#release-bar)

## Boundary

The candidate pull request remains unmerged until release authorization. It
contains stabilization and release preparation only, does not introduce a new
post-MVP capability, and does not create a tag, NuGet publication, or GitHub
Release.

## Acceptance

- User guidance and release notes describe the supported MVP accurately,
  identify the tested compatibility matrix, and keep every beyond-MVP feature
  explicitly outside the 1.0.0 contract.
- The final release-bar certification passes for this exact commit and exact
  candidate package; no evidence is inherited from a different build after a
  release-affecting change.
- Every 0.9.0 blocker and stabilization finding has a closed atomic issue and
  no known release-blocking issue or unresolved P0–P2 review finding remains.
- Package identity, command name, schema compatibility, SemVer behavior,
  metadata, symbols, checksums, installation forms, support statements, and
  public examples are internally consistent.
- Candidate artifacts, canonical checks, complete compatibility and security
  matrices, performance evidence, independent agent gates, and CI pass for the
  proposed commit.
- The release instructions identify the exact approved commit, and version
  `1.0.0` is not already published.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no `1.0.0` release-side
  state exists.

## Dependencies

- `MVP-E12-S31`
- `MVP-E13-S32`

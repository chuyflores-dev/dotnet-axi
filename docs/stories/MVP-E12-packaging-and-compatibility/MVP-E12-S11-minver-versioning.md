# MVP-E12-S11 — Derive Versions from Release Tags

## Outcome

Release tags are the single authority for package and installed-tool versions,
while untagged builds remain visibly non-publishable prereleases.

## Design

- [Releases and versioning](../../design/releases.md#version-authorities)

## Boundary

This story derives and verifies versions. It does not create a release tag or
publish an artifact.

## Acceptance

- A `v<semver>` tag on the current commit produces exactly that package,
  assembly, and CLI version.
- Untagged `main` and pull-request builds produce a prerelease version in the
  current planned minor line and cannot be mistaken for a release artifact.
- CI fetches enough Git history for tag-derived versioning, and candidate
  verification can request an exact non-publishable version override.
- After a stable minor tag, ordinary builds move to the next planned minor
  prerelease without per-merge version edits.

## Verification

- Automated checks cover no-tag, prefixed stable tag, prefixed prerelease tag,
  candidate override, and post-release commit cases.
- Package metadata and `dnaxi --version` agree with the calculated version.

## Dependencies

- `MVP-E01-S10`
- `MVP-E12-S09`

# MVP-E12-S13 — Protect Release-tag Creation

## Outcome

An authorized GitHub Release creates one version tag on an exact commit from
`main`, and the published release makes that tag immutable.

## Design

- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

Configuring and validating the release path does not authorize creating a tag
or publishing a GitHub Release.

## Acceptance

- The release command identifies an exact commit from `main` and a
  `v<SemVer>` tag.
- The release workflow refuses malformed tags, commits outside `main`, and
  tags that disagree with the release event.
- Published GitHub Releases are immutable, so their tags cannot move or be
  replaced.
- Repeated publication uses the existing release identity rather than creating
  a second tag.

## Verification

- Release-candidate verification exercises the non-publishing gates without
  creating a ref.
- Workflow identity checks and repository release settings are inspected
  without creating a GitHub Release.

## Dependencies

- `MVP-E12-S11`
- `MVP-E12-S12`

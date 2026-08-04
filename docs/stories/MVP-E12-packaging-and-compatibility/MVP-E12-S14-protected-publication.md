# MVP-E12-S14 — Publish from a GitHub Release

## Outcome

A published GitHub Release selects one tagged commit for verified, protected
NuGet publication.

## Design

- [Verification and publishing boundary](../../design/releases.md#verification-and-publishing-boundary)
- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

Pull-request and `main` CI remain unable to publish. Implementing the workflow
or running candidate verification does not authorize a GitHub Release or a
NuGet push.

## Acceptance

- Only a published GitHub Release with a `v<SemVer>` tag on a commit reachable
  from `main` can select a release identity.
- The reusable release-candidate workflow completes tests, package inspection,
  installation, and cross-platform invocation before publication.
- The protected job consumes that exact candidate and publishes its package
  and symbols; an existing NuGet version fails rather than being skipped.
- Repository access remains read-only, and only the final push step receives
  the NuGet credential.

## Verification

- A manually dispatched release-candidate run rehearses every non-publishing
  package gate without requesting a credential or creating release state.
- A focused, non-publishing CI verifier covers invalid tags, commits outside
  `main`, failed candidate checks, and the protected push contract.

## Dependencies

- `MVP-E12-S01`
- `MVP-E12-S02`
- `MVP-E12-S11`
- `MVP-E12-S12`

# MVP-E12-S15 — Configure Trusted NuGet Publishing

## Outcome

External publication uses a protected GitHub environment and short-lived
NuGet credentials bound to this repository and release workflow.

## Design

- [Verification and publishing boundary](../../design/releases.md#verification-and-publishing-boundary)

## Boundary

Configuration establishes trust but does not create a tag, publish a package,
or approve a release deployment.

## Acceptance

- Published GitHub Releases are immutable, and the protected `release`
  environment requires manual approval for `v*` tags only.
- The NuGet trusted-publishing policy identifies the repository, publication
  workflow, environment, and intended package owner.
- The publication job requests only short-lived OIDC credentials immediately
  before push; no long-lived NuGet API key is available to build or test jobs.
- A documented package-scoped, expiring-key fallback exists only if trusted
  publishing is unavailable to the selected NuGet owner.

## Verification

- Repository environment settings and workflow permissions match the trusted
  publisher policy without exposing secret values.
- The focused release verifier proves that non-publishing jobs neither request
  OIDC permission nor receive a NuGet credential.

## Dependencies

- `MVP-E12-S14`

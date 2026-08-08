# MVP-E12-S29 — Publish and Verify 0.8.0

## Outcome

The verified `dnaxi` `0.8.0` package and matching GitHub Release are publicly
available with repository configuration, freshness, and configurable
validation.

## Design

- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

This is the release action. It remains blocked until the user explicitly
authorizes publication after approving the completed release candidate.

## Acceptance

- The candidate pull request is merged without unrelated changes, and its
  exact release commit receives immutable tag `v0.8.0`.
- The published GitHub Release triggers protected publication of the matching
  package and symbols.
- Fresh public-source `dnx`, global, and local invocations report `0.8.0` and
  exercise configuration validation and plan explanation, freshness changes,
  affected scope, and fast and standard validation without source writes or
  claims for deferred setup and full-profile behavior.
- Benchmark links and claims match the approved candidate evidence, and the
  `0.8.0` milestone closes only after public verification succeeds.

## Verification

- NuGet ownership, indexing, metadata, symbols, install, update, uninstall,
  and one-shot execution are verified from clean temporary stores.
- GitHub tag, release, NuGet package, commit, versions, and release notes agree.

## Dependencies

- `MVP-E12-S28`

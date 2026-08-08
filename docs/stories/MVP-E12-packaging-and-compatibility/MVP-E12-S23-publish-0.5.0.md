# MVP-E12-S23 — Publish and Verify 0.5.0

## Outcome

The verified `dnaxi` `0.5.0` package and matching GitHub Release are publicly
available with stable symbol identity and bounded context.

## Design

- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

This is the release action. It remains blocked until the user explicitly
authorizes publication after approving the completed release candidate.

## Acceptance

- The candidate pull request is merged without unrelated changes, and its
  exact release commit receives immutable tag `v0.5.0`.
- The published GitHub Release triggers protected publication of the matching
  package and symbols.
- Fresh public-source `dnx`, global, and local invocations report `0.5.0` and
  exercise symbol discovery, identity resolution, show, outline, and bounded
  context without claiming deferred relationships.
- Benchmark links and claims match the approved candidate evidence, and the
  `0.5.0` milestone closes only after public verification succeeds.

## Verification

- NuGet ownership, indexing, metadata, symbols, install, update, uninstall,
  and one-shot execution are verified from clean temporary stores.
- GitHub tag, release, NuGet package, commit, versions, and release notes agree.

## Dependencies

- `MVP-E12-S22`

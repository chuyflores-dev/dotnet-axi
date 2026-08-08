# MVP-E12-S21 — Publish and Verify 0.4.0

## Outcome

The verified `dnaxi` `0.4.0` package and matching GitHub Release are publicly
available as the primary no-install `dnx` path for source discovery.

## Design

- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

This is the release action. It remains blocked until the user explicitly
authorizes publication after approving the completed release candidate.

## Acceptance

- The candidate pull request is merged without unrelated changes, and its
  exact release commit receives immutable tag `v0.4.0`.
- The published GitHub Release triggers protected publication of the matching
  package and symbols.
- A fresh public-source invocation of
  `dnx dnaxi@0.4.0 --verbosity quiet --` reports `0.4.0` and exercises home,
  help, file, text, and stable-syntax discovery before secondary global and
  local compatibility smokes run.
- Public guidance and NuGet metadata direct users of `dotnet-axi` 0.3.0 to
  `dnaxi` without changing or republishing the earlier package.
- Benchmark links and claims match the approved candidate evidence, and the
  `0.4.0` milestone closes only after public verification succeeds.

## Verification

- NuGet ownership, indexing, metadata, symbols, exact version-pinned `dnx`,
  install, update, and uninstall are verified from clean temporary stores.
- GitHub tag, release, NuGet package, commit, versions, and release notes agree.

## Dependencies

- `MVP-E12-S20`

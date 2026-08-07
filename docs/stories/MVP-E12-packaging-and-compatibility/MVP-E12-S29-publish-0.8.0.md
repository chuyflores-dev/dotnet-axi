# MVP-E12-S29 — Publish and Verify 0.8.0

## Outcome

The verified `dotnet-axi` `0.8.0` package and matching GitHub Release are
publicly available with safe Claude Code and Codex integration.

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
- Fresh public-source global, local, and `dnx` invocations report `0.8.0`; in
  isolated temporary Claude Code and Codex configurations, setup, bounded
  passive context, repair, removal, artifact cleanup, and constrained failure
  reporting work without changing real user configuration or claiming
  OpenCode support.
- Codex and advisory Claude benchmark links and claims match the approved
  candidate evidence, and the `0.8.0` milestone closes only after public
  verification succeeds.

## Verification

- NuGet ownership, indexing, metadata, symbols, install, update, uninstall,
  and one-shot execution are verified from clean temporary stores.
- GitHub tag, release, NuGet package, commit, versions, and release notes agree.

## Dependencies

- `MVP-E12-S28`

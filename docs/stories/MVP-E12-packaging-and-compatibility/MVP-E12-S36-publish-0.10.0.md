# MVP-E12-S36 — Publish and Verify 0.10.0

## Outcome

The verified `dnaxi` `0.10.0` feature-complete MVP preview package and
matching GitHub Release are publicly available with its exact release-gate
evidence.

## Design

- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

This is the release action. It remains blocked until the user explicitly
authorizes publication after approving the completed release candidate.
Publication does not convert disclosed 1.0.0 blockers into passing gates.

## Acceptance

- The candidate pull request is merged without unrelated changes, and its
  exact release commit receives immutable tag `v0.10.0`.
- The published GitHub Release triggers protected publication of the matching
  package and symbols.
- Fresh public-source `dnx`, global, and local invocations report `0.10.0` and
  pass the declared package, platform, SDK, optional-dependency, and
  constrained-host public verification applicable after publication.
- Compatibility manifests, correctness/security/performance reports, and
  independent Codex and Claude evidence link to the exact published commit,
  package, schemas, environments, agents, models, and harnesses.
- The `0.10.0` milestone closes only after public verification succeeds and
  all remaining 1.0.0 stabilization blockers are explicitly tracked.

## Verification

- NuGet ownership, indexing, metadata, symbols, install, update, uninstall,
  and one-shot execution are verified from clean temporary stores.
- GitHub tag, release, NuGet package, commit, versions, release notes,
  compatibility manifest, and release-gate report agree.

## Dependencies

- `MVP-E12-S35`

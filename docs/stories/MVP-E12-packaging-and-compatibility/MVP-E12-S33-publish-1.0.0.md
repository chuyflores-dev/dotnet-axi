# MVP-E12-S33 — Publish and Verify 1.0.0

## Outcome

The supported `dotnet-axi` `1.0.0` package and matching GitHub Release are
publicly available with passing release-bar evidence and no known blocker.

## Design

- [Release procedure](../../design/releases.md#release-procedure)
- [1.0.0 release outcome](../../design/releases.md#planned-capability-milestones)

## Boundary

This is the release action. It remains blocked until the user explicitly
authorizes publication after approving the completed release candidate. No
failed, missing, stale, or incomparable gate may be waived for publication.

## Acceptance

- The candidate pull request is merged without unrelated changes, and its
  exact certified release commit receives immutable tag `v1.0.0`.
- The published GitHub Release triggers protected publication of the matching
  package and symbols from the already verified candidate artifacts.
- Fresh public-source global, local, and `dnx` invocations report `1.0.0` and
  pass the declared package, platform, SDK, optional-dependency,
  constrained-host, setup, validation, and representative capability checks.
- Compatibility manifests, correctness/security/performance reports, and
  independent Codex and Claude evidence link to the exact public commit,
  package, schemas, environments, agents, models, and harnesses.
- NuGet metadata, README, support boundary, release notes, tag, package,
  symbols, and GitHub Release agree, and the `1.0.0` milestone closes only
  after every public verification succeeds.

## Verification

- NuGet ownership, indexing, metadata, symbols, install, update, uninstall,
  and one-shot execution are verified from clean temporary stores.
- The final public verification report proves no artifact, version, evidence,
  compatibility, or documentation drift occurred after certification.

## Dependencies

- `MVP-E12-S32`

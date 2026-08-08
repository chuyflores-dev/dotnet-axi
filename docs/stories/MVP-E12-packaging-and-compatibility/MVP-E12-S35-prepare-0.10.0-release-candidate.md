# MVP-E12-S35 — Prepare the 0.10.0 Release Candidate

## Outcome

One reviewed commit is ready to become the feature-complete `v0.10.0` MVP
preview, with compatibility matrices and complete release-gate evidence.

## Design

- [0.10.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)
- [Release bar](../../../REQUIREMENTS.md#release-bar)

## Boundary

The candidate pull request remains unmerged until release authorization. This
story does not create `v0.10.0`, publish to NuGet, or hide a failing release
gate that must be resolved before 1.0.0.

## Acceptance

- User guidance names `0.10.0` and describes the complete implemented MVP
  preview without claiming general source refactoring, OpenCode setup, a warm
  daemon, direct Tree-sitter bindings, or a persistent semantic graph.
- Release evidence identifies the exact OS/RID, SDK feature bands,
  MSBuild/Roslyn hosts, Git and `rg` versions, invocation forms, optional
  dependency states, constrained-host cases, and package artifacts tested.
- Correctness, TOON, platform, security, freshness, performance, and complete
  Codex and Claude agent-task gates are present and evaluated; the two agent
  series remain independent and exact-configuration scoped.
- Every failing, missing, or incomparable release-bar result is named as an
  explicit 1.0.0 blocker rather than omitted, weakened, or converted to
  success by prose.
- Candidate artifacts, checksums, symbols, metadata, installation smoke tests,
  canonical checks, compatibility matrices, and CI pass for the proposed
  commit.
- The release instructions identify the exact approved commit, and version
  `0.10.0` is not already published.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no `0.10.0`
  release-side state exists.

## Dependencies

- `MVP-E12-S31`
- `MVP-E13-S12`

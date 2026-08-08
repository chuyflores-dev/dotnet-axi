# MVP-E12-S30 — Prepare the 0.9.0 Release Candidate

## Outcome

One reviewed commit is ready to become `v0.9.0`, with truthful safe
agent-integration documentation, scoped Codex and advisory Claude evidence,
and passing checks.

## Design

- [0.9.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The candidate pull request remains unmerged until release authorization. This
story does not create `v0.9.0`, publish to NuGet, or create a GitHub Release.

## Acceptance

- User guidance names `0.9.0` and describes only verified Claude Code and
  Codex setup, bounded passive context, repair, removal, effects, process and
  secret safety, diagnostic artifacts, cleanup, and constrained-host results.
- Documentation distinguishes repository and user scope, supported and
  unknown formats, invocation repair, trust review, managed policy, exact
  changed targets, recoverable backups, and the unsupported OpenCode result.
- Release notes explicitly defer the full platform/SDK/restricted-host matrix,
  release-level security and performance evidence, and full independent Codex
  and Claude release gates to `0.10.0`.
- The affected Codex and advisory Claude series are complete, independently
  reported, and scoped to their exact agent, model, harness, corpus,
  permissions, setup effects, and comparison conditions.
- Candidate artifacts, checksums, symbols, metadata, installation smoke tests,
  canonical checks, and CI pass for the proposed commit.
- The release instructions identify the exact approved commit, and version
  `0.9.0` is not already published.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no `0.9.0` release-side
  state exists.

## Dependencies

- `MVP-E12-S29`
- `MVP-E13-S26`
- `MVP-E13-S27`

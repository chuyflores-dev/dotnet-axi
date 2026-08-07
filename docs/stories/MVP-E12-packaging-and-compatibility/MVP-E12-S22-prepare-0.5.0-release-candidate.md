# MVP-E12-S22 — Prepare the 0.5.0 Release Candidate

## Outcome

One reviewed commit is ready to become `v0.5.0`, with truthful semantic
relationship and graph documentation, scoped Codex evidence, and passing
checks.

## Design

- [0.5.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The candidate pull request remains unmerged until release authorization. This
story does not create `v0.5.0`, publish to NuGet, or create a GitHub Release.

## Acceptance

- User guidance names `0.5.0` and describes only verified references,
  implementations, overrides, derived types, callers, callees, relationship
  context, project graphs, cycles, paths, and impact.
- Release notes explicitly defer static analysis and structured SDK execution
  to `0.6.0`.
- The affected Codex benchmark subset is complete and every result or claim is
  scoped to the exact model, harness, corpus, command activation, and
  comparison condition.
- Candidate artifacts, checksums, metadata, installation smoke tests,
  canonical checks, and CI pass for the proposed commit.
- The release instructions identify the exact approved commit, and version
  `0.5.0` is not already published.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no `0.5.0` release-side
  state exists.

## Dependencies

- `MVP-E12-S21`
- `MVP-E09-S15`
- `MVP-E13-S20`

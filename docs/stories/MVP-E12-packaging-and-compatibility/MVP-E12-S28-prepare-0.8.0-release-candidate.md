# MVP-E12-S28 — Prepare the 0.8.0 Release Candidate

## Outcome

One reviewed commit is ready to become `v0.8.0`, with truthful configurable
validation documentation, scoped Codex evidence, and passing checks.

## Design

- [0.8.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The candidate pull request remains unmerged until release authorization. This
story does not create `v0.8.0`, publish to NuGet, or create a GitHub Release.

## Acceptance

- User guidance names `0.8.0` and describes only verified configuration,
  freshness, affected-scope, and fast or standard validation behavior.
- Documentation explains precedence and plan sources, freshness inputs,
  profile effects, test-runner and zero-test policy, partial or unavailable
  scope, lifecycle states, child exits, and protected diagnostic artifacts.
- Release notes explicitly defer safe agent setup and repair to `0.9.0` and
  full validation, package or vulnerability policy, and general source
  modification beyond the MVP.
- The affected Codex series is complete and every result or claim is scoped
  to the exact model, harness, corpus, skill activation, configuration,
  effects, and comparison condition.
- Candidate artifacts, checksums, metadata, installation smoke tests,
  canonical checks, and CI pass for the proposed commit.
- The release instructions identify the exact approved commit, and version
  `0.8.0` is not already published.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no `0.8.0` release-side
  state exists.

## Dependencies

- `MVP-E12-S27`
- `MVP-E09-S17`
- `MVP-E13-S24`

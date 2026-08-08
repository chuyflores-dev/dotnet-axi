# MVP-E12-S26 — Prepare the 0.7.0 Release Candidate

## Outcome

One reviewed commit is ready to become `v0.7.0`, with truthful static-analysis
and structured SDK-execution documentation, scoped Codex evidence, and
passing checks.

## Design

- [0.7.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The candidate pull request remains unmerged until release authorization. This
story does not create `v0.7.0`, publish to NuGet, or create a GitHub Release.

## Acceptance

- User guidance names `0.7.0` and describes only verified compiler,
  configured-analyzer, structural, architecture, changed-scope, restore,
  build, test, format, and constrained `dotnet` behavior.
- Documentation distinguishes passive and executing analysis, format check and
  apply, dependency exits and public exits, protected artifacts, and every
  applicable network, repository-code, and write effect.
- Release notes explicitly defer configurable validation profiles and their
  remaining configuration/freshness behavior to `0.8.0`.
- The affected Codex benchmark subset is complete and every result or claim is
  scoped to the exact model, harness, corpus, command activation, effects, and
  comparison condition.
- Candidate artifacts, checksums, metadata, installation smoke tests,
  canonical checks, and CI pass for the proposed commit.
- The release instructions identify the exact approved commit, and version
  `0.7.0` is not already published.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no `0.7.0` release-side
  state exists.

## Dependencies

- `MVP-E12-S25`
- `MVP-E09-S16`
- `MVP-E13-S22`

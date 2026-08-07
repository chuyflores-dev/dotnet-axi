# MVP-E12-S18 — Prepare the 0.3.0 Release Candidate

## Outcome

One reviewed commit is ready to become `v0.3.0`, with truthful discovery
documentation, scoped Codex benchmark evidence, and passing candidate checks.

## Design

- [0.3.0 release outcome](../../design/releases.md#planned-capability-milestones)
- [Release procedure](../../design/releases.md#release-procedure)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The candidate pull request remains unmerged until release authorization. This
story does not create `v0.3.0`, publish to NuGet, or create a GitHub Release.

## Acceptance

- Installation, global, local, and `dnx` examples name `0.3.0` and expose only
  the file, text, and stable syntax behavior actually verified.
- Release notes describe optional text-engine fallback and known limitations
  and scope every benchmark statement to the exact Codex model and harness.
- The first measured discovery comparison is complete; a result below the
  improvement threshold remains visible and is not rewritten as a claim.
- Candidate artifacts, checksums, metadata, installation smoke tests,
  canonical checks, and CI pass for the proposed commit.
- The release instructions identify the exact approved commit, the NuGet
  package is controlled by the configured owner, and version `0.3.0` is not
  already published.

## Verification

- The implementation review gate completes with no confirmed P0–P2 findings
  in at most three clean-context rounds.
- The approved candidate pull request is left open and no `0.3.0` release-side
  state exists.

## Dependencies

- `MVP-E12-S17`
- `MVP-E11-S02`
- `MVP-E11-S04`
- `MVP-E11-S12`
- `MVP-E11-S13`
- `MVP-E12-S03`
- `MVP-E12-S04`
- `MVP-E09-S12`
- `MVP-E13-S15`

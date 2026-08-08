# MVP-E13-S30 — Measure the 1.0.0 Codex Release Gate

## Outcome

A manually dispatched Codex series proves the complete applicable MVP task
corpus against the exact proposed 1.0.0 package and Agent Skill.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Release bar](../../../REQUIREMENTS.md#release-bar)

## Boundary

This story measures the stabilized release candidate; it does not absorb
release defects into one non-atomic task. Any discovered product, corpus,
harness, or evidence blocker receives its own linked issue and invalidates the
series until the corrected candidate is measured from the beginning.

## Acceptance

- Every applicable MVP task runs at least five times per baseline and
  candidate condition with randomized interleaving, equivalent isolated
  workspaces, and complete deterministic success and safety oracles.
- The immutable series pins the proposed 1.0.0 package, repository skill, exact
  candidate commit, fixture commit, schemas, harness, adapter, Codex
  executable, exact model and reasoning, instructions, tools, sandbox,
  approval, locale, time zone, network policy, run count, and seed.
- All required trajectories and reconciliations are complete; no run is
  missing, drifted, failed, timed out, or incomparable.
- The series has no safety-critical regression, aggregate success is at least
  the raw-tool baseline, and median total token use is at least 10% lower for
  the tested Codex/model/harness configuration.
- Tool-call, activation, permission, source-write, network,
  workspace-integrity, validation, scope, and completion-claim checks pass
  without waived or smoothed evidence.

## Verification

- Normalized results reconcile with raw Codex events, corpus oracles, versions,
  hashes, schedules, usage, tools, file changes, validation evidence, and the
  exact approved 1.0.0 series manifest.

## Dependencies

- `MVP-E12-S31`
- `MVP-E13-S28`

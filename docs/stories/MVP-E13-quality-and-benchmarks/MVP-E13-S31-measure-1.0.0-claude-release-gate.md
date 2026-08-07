# MVP-E13-S31 — Measure the 1.0.0 Claude Release Gate

## Outcome

A manually dispatched Claude series independently proves the same complete
applicable MVP task corpus against the exact proposed 1.0.0 package and Agent
Skill.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Release bar](../../../REQUIREMENTS.md#release-bar)

## Boundary

Claude evidence remains a separate exact-agent and exact-model series and is
never pooled with Codex. Any discovered product, corpus, harness, adapter, or
evidence blocker receives its own linked issue and invalidates the series
until the corrected candidate is measured from the beginning.

## Acceptance

- Every applicable MVP task runs at least five times per baseline and
  candidate condition with the same approved task definitions, applicability,
  effects, repetitions, oracles, package, skill, and controlled fixture state
  as the Codex release series.
- The immutable series pins the proposed 1.0.0 package, exact candidate and
  fixture commits, schemas, harness, adapter, Claude Code executable, exact
  model, permission mode, allowed tools, turn limit, instructions, locale,
  time zone, network policy, run count, and seed.
- All required trajectories and reconciliations are complete; no run is
  missing, drifted, failed, stalled, timed out, or incomparable.
- The series has no safety-critical regression, aggregate success is at least
  the raw-tool baseline, and median total token use is at least 10% lower for
  the tested Claude/model/harness configuration.
- Tool-call, activation, permission, source-write, network,
  workspace-integrity, validation, scope, worker-lifecycle, and
  completion-claim checks pass without waived or smoothed evidence.

## Verification

- Normalized results reconcile with raw Claude events, corpus oracles,
  versions, hashes, schedules, usage and cost, tools, file changes, validation
  evidence, worker lifecycle, and the exact approved 1.0.0 series manifest.

## Dependencies

- `MVP-E13-S29`
- `MVP-E13-S30`

# MVP-E13-S29 — Measure the 0.9.0 Claude Release Gate

## Outcome

A manually dispatched Claude series independently evaluates the same complete
applicable MVP task corpus against the proposed 0.9.0 package and Agent Skill.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Release bar](../../../REQUIREMENTS.md#release-bar)

## Boundary

Claude results remain scoped to the exact Claude Code CLI, model, permission
configuration, harness, and corpus. They are never pooled with Codex or used
to retroactively alter the shared tasks or Codex series.

## Acceptance

- Every applicable corpus task through 0.8.0 runs at least five times per
  baseline and candidate condition using the same approved task definitions,
  applicability, effects, repetitions, oracles, package, skill, and controlled
  fixture state as the Codex release series.
- The immutable series pins the proposed 0.9.0 package, product and fixture
  commits, schemas, harness, adapter, Claude Code executable, exact model,
  permission mode, allowed tools, turn limit, instructions, locale, time zone,
  network policy, run count, and randomization seed.
- The report publishes per-task and aggregate correctness, safety, usage,
  cost, tool-call, turn, duration, inspected-scope, validation, worker
  lifecycle, and raw-event evidence without smoothing missing, failed,
  stalled, timed-out, or incomparable runs.
- Safety-critical regressions, aggregate success, median total-token
  reduction, tool-call regression, activation, permission, source-write,
  network, workspace-integrity, and completion-claim thresholds are evaluated
  independently from Codex.
- A passing or failing result remains explicit; the series cannot satisfy a
  release claim unless every required trajectory and reconciliation check is
  complete.

## Verification

- Normalized results reconcile with raw Claude events, corpus oracles,
  versions, hashes, schedules, usage and cost, tools, file changes, validation
  evidence, worker lifecycle, and the exact approved 0.9.0 series manifest.

## Dependencies

- `MVP-E13-S14`
- `MVP-E13-S27`
- `MVP-E13-S28`

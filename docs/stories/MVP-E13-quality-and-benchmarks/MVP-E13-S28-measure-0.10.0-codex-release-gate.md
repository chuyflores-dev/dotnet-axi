# MVP-E13-S28 — Measure the 0.10.0 Codex Release Gate

## Outcome

A manually dispatched Codex series evaluates the complete applicable MVP task
corpus against the proposed 0.10.0 package and Agent Skill.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Release bar](../../../REQUIREMENTS.md#release-bar)

## Boundary

Codex results remain scoped to the exact Codex CLI, model, reasoning setting,
harness, corpus, and permission profile. They are not pooled with Claude or
used to claim untested models, hosts, or deferred post-MVP capabilities.

## Acceptance

- Every applicable corpus task through 0.9.0 runs at least five times per
  baseline and candidate condition with randomized interleaving, equivalent
  isolated workspaces, and the complete shared success and safety oracles.
- The immutable series pins the proposed 0.10.0 package, repository skill,
  product and fixture commits, schemas, harness, adapter, Codex executable,
  exact model and reasoning, instructions, tools, sandbox, approval, locale,
  time zone, network policy, run count, and randomization seed.
- The report publishes per-task and aggregate correctness, safety, token,
  tool-call, turn, duration, inspected-scope, validation, and raw-trajectory
  evidence without smoothing missing, failed, timed-out, or incomparable runs.
- Safety-critical regressions, aggregate success, median total-token
  reduction, tool-call regression, activation, permission, source-write,
  network, workspace-integrity, and completion-claim thresholds are evaluated
  exactly as documented.
- A passing or failing result remains explicit; the series cannot satisfy a
  release claim unless every required trajectory and reconciliation check is
  complete.

## Verification

- Normalized results reconcile with raw Codex events, corpus oracles, versions,
  hashes, schedules, usage, tools, file changes, validation evidence, and the
  exact approved 0.10.0 series manifest.

## Dependencies

- `MVP-E13-S18`
- `MVP-E13-S26`

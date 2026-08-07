# MVP-E13-S27 — Measure the 0.8.0 Claude Subset

## Outcome

A manually dispatched advisory Claude series measures the same 0.8.0 safe
agent-integration task subset and condition boundaries as Codex.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Claude results remain a separate exact-agent and exact-model series. They are
not pooled with Codex, treated as the full 0.9.0 dual-agent gate, or used to
retroactively change the shared corpus or Codex condition.

## Acceptance

- Every affected 0.8.0 task runs at least five times per baseline and candidate
  condition with the same approved task definitions, applicability, effects,
  repetitions, oracles, and controlled fixture state as the Codex series.
- The manifest pins the Claude Code CLI, exact model, permission mode, allowed
  tools, turn limit, loaded instructions, network policy, adapter, harness,
  corpus, and condition-specific exposure.
- Setup writes are confined to fixture-owned repository and user roots;
  credentials and ambient Claude configuration never enter prompts, fixtures,
  trajectories, or published evidence.
- The report retains raw events, usage, cost, turns, tool activity, setup and
  safety evidence, timeout and worker lifecycle, validation, and every missing
  or incomparable run.
- Advisory safety, correctness, activation, regression, and efficiency results
  are reported independently from Codex without a combined product-effect
  number.

## Verification

- Normalized results reconcile with raw Claude events, task oracles, versions,
  hashes, permission and tool settings, setup targets, file changes, worker
  lifecycle, and the exact approved 0.8.0 series manifest.

## Dependencies

- `MVP-E13-S14`
- `MVP-E13-S25`
- `MVP-E13-S26`

# MVP-E13-S14 — Add the Claude Benchmark Adapter

## Outcome

The established benchmark protocol can run the same raw-tool and
`dotnet-axi` conditions with a pinned Claude Code CLI and exact model.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Claude results form a separate agent/model series and are never pooled with
Codex metrics or used to retroactively tune the shared task corpus.

## Acceptance

- The adapter uses supported noninteractive streaming output and captures raw
  events, usage, cost, turns, final response, tool activity, exit state, and
  timeout.
- Worker monitoring distinguishes observable progress from a bounded stall, preserves the first permission failure, and never starts a duplicate worker while one remains live.
- The run manifest pins the Claude Code CLI version, exact model, permission
  mode, allowed tools, turn limit, loaded instructions, and network policy.
- It runs the same applicable task definitions, baseline/candidate boundaries,
  randomization, repetitions, and oracles used by the Codex series.
- Initial Claude evidence is advisory; full `0.9.0` evidence reports Codex and
  Claude results independently.

## Verification

- Contract fixtures cover successful, permission-denied, read-only, network-denied, stalled, timed-out, truncated, and malformed Claude event streams.
- A manually dispatched smoke run proves that normalized metrics reconcile
  with the captured raw events.

## Dependencies

- `MVP-E13-S13`

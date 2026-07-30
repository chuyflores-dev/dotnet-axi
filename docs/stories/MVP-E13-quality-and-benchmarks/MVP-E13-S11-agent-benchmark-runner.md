# MVP-E13-S11 — Build the Agent Benchmark Runner

## Outcome

Agent adapters can run interleaved raw-tool baseline and `dotnet-axi`
candidate conditions through one normalized benchmark protocol.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The harness records tool-owned benchmark data and does not make product
telemetry or transcript capture part of normal CLI operation. This story builds
the runner and fake adapter; real Codex and Claude execution belong to their
adapter stories.

## Acceptance

- Each task/condition runs at least five times with randomized interleaving and
  identical model, settings, task state, permissions, and network policy.
- Results capture success, safety, tokens, turns, calls, duration, inspected
  scope, validation, versions, hashes, order, timeout, and raw trajectory
  evidence.
- Adapter inputs and normalized results are agent-neutral, while raw
  provider-specific events remain available as immutable evidence.
- Real-agent runs are manually dispatched; CI exercises the runner only with a
  deterministic fake adapter.

## Verification

- Harness self-tests use a deterministic fake agent to validate randomization,
  parity, metric capture, retries, timeouts, and raw-result integrity.

## Dependencies

- `MVP-E13-S10`
- `MVP-E09-S07`

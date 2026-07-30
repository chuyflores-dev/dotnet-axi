# MVP-E13-S11 — Run Agent Benchmark Comparisons

## Outcome

The same agent and model can run interleaved raw-tool baseline and
`dotnet-axi` candidate conditions with complete trajectory metrics.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The harness records tool-owned benchmark data and does not make product
telemetry or transcript capture part of normal CLI operation.

## Acceptance

- Each task/condition runs at least five times with randomized interleaving and
  identical model, settings, task state, permissions, and network policy.
- Results capture success, safety, tokens, turns, calls, duration, inspected
  scope, validation, versions, hashes, order, timeout, and raw trajectory
  evidence.

## Verification

- Harness self-tests use a deterministic fake agent to validate randomization,
  parity, metric capture, retries, timeouts, and raw-result integrity.

## Dependencies

- `MVP-E13-S10`
- `MVP-E09-S07`
- `MVP-E07-S04`
- `MVP-E07-S06`

# MVP-E13-S13 — Add the Codex Benchmark Adapter

## Outcome

The benchmark runner can compare raw-tool and `dotnet-axi` conditions using a
pinned Codex CLI, model, reasoning setting, and controlled execution policy.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)

## Boundary

The adapter automates benchmark runs through Codex's supported noninteractive
interface. It does not change Codex configuration, select a model alias as a
durable baseline, or run paid benchmarks on pull-request CI.

## Acceptance

- The adapter uses ephemeral machine-readable execution and captures the raw
  event stream, reported usage, final response, commands, file changes, exit
  state, and timeout.
- Every run selects its sandbox explicitly: write-capable tasks use a declared writable workspace root, while passive tasks use `read-only`.
- One process identity owns each run; event silence does not start a replacement, and liveness plus a total timeout determine bounded termination.
- The run manifest pins the Codex CLI version, exact model, reasoning setting,
  sandbox, permissions, loaded instructions, and network policy.
- Baseline and candidate conditions use isolated equivalent workspaces and
  differ only by the declared `dotnet-axi` skill/tool exposure.
- The first measured series covers applicable source-discovery tasks and
  publishes condition-blinded outcomes plus complete reproducibility metadata.

## Verification

- Contract fixtures cover successful, permission-denied, read-only, network-denied, stalled, timed-out, truncated, and malformed Codex event streams.
- A manually dispatched smoke run proves that normalized metrics reconcile
  with the captured raw events.

## Dependencies

- `MVP-E13-S11`
- `MVP-E03-S02`
- `MVP-E03-S03`

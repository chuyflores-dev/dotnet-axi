# MVP-E13-S15 — Measure 0.3.0 Codex Discovery Tasks

## Outcome

A reproducible measured series compares raw-tool and `dotnet-axi` conditions
for the 0.3.0 source-discovery corpus using one pinned Codex configuration.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

This manually dispatched evidence run does not execute on pull-request CI. A
completed comparison is not automatically an improvement claim; every claim
must satisfy the documented threshold and remain scoped to the exact model,
settings, harness, and corpus.

## Acceptance

- Every applicable file, text, and stable syntax task runs at
  least five times per condition with randomized interleaving and equivalent
  isolated workspaces.
- The manifest pins agent, model, reasoning, CLI, sandbox, permissions,
  network, instructions, corpus, fixture, package, schema, and commit identity.
- Condition-blinded outcomes, safety, tokens, turns, tool calls, duration,
  inspected scope, validation, timeouts, and immutable raw trajectories are
  retained with a reproducible summary.
- The report evaluates the documented regression and improvement thresholds
  and labels failed, missing, or incomparable evidence without smoothing it
  into a success.

## Verification

- Normalized metrics reconcile with raw Codex events, and a clean rerun can be
  prepared from the retained manifest and hashes.

## Dependencies

- `MVP-E13-S10`
- `MVP-E13-S11`
- `MVP-E13-S13`
- `MVP-E09-S12`

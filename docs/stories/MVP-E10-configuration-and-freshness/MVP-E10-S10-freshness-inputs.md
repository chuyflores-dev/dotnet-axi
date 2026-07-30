# MVP-E10-S10 — Track Freshness Inputs

## Outcome

Loaded MSBuild, Roslyn, syntax, graph, and adapter state records every observed
input that can affect its result.

## Design

- [Freshness and cache](../../design/runtime-and-distribution.md#freshness-and-cache)
- [Snapshot identity](../../design/workspace.md#snapshot-identity)

## Boundary

Modification time alone never proves freshness, and unobserved inputs cannot
be represented by a snapshot.

## Acceptance

- Source, linked/additional/generated inputs, projects/imports, configuration,
  assets, editor settings, SDKs, properties, and adapter versions can
  invalidate only affected state.
- Uncommitted and untracked content participates through content identity.

## Verification

- Freshness tests mutate each documented input independently and assert the
  affected state reloads while unrelated state may remain reusable.

## Dependencies

- `MVP-E02-S08`
- `MVP-E10-S02`

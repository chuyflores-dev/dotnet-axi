# MVP-E10-S11 — Reuse Disposable State

## Outcome

One process can safely reuse loaded state, while deleting all tool-owned state
changes performance only.

## Design

- [Freshness and cache](../../design/runtime-and-distribution.md#freshness-and-cache)
- [Performance principles](../../design/quality.md#performance-principles)

## Boundary

The MVP makes no daemon, mandatory persistent cache, or cross-process warm
performance claim.

## Acceptance

- Safe MSBuild, Roslyn, syntax, resolution, and graph objects are reused only
  while their freshness inputs remain valid.
- Fresh execution after state deletion returns semantically equivalent results
  and resolves unchanged entity IDs.

## Verification

- Repeated-command tests compare warm in-process, invalidated, fresh-process,
  and state-deleted results.

## Dependencies

- `MVP-E10-S10`
- `MVP-E04-S03`

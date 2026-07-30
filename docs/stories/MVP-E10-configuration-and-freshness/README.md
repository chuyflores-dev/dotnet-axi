# MVP-E10 — Configuration and Freshness

## Outcome

Commands use validated repository configuration and current worktree inputs
without correctness depending on retained tool state.

## Scope

- Root `dotnet-axi.yml` schema, validation, and path handling.
- CLI, repository, derived-value, and default precedence.
- Effective-plan explanation and validation-profile configuration.
- Content-based invalidation and safe in-process reuse.
- Disposable, isolated state behavior.

## Boundary

A persistent cache, warm daemon, and cross-process performance claims are not
part of the MVP.

## Design

- [Runtime and distribution](../../design/runtime-and-distribution.md)
- [Workspace](../../design/workspace.md)
- [Design foundations](../../design/foundations.md)

## Dependencies

- `MVP-E01`
- `MVP-E02`

## Stories

- [MVP-E10-S01 — Locate repository configuration](MVP-E10-S01-config-discovery.md)
- [MVP-E10-S02 — Parse configuration schema v1](MVP-E10-S02-config-schema.md)
- [MVP-E10-S03 — Validate configuration](MVP-E10-S03-config-validation.md)
- [MVP-E10-S04 — Resolve configured paths](MVP-E10-S04-config-paths.md)
- [MVP-E10-S05 — Apply configuration precedence](MVP-E10-S05-config-precedence.md)
- [MVP-E10-S06 — Bind search configuration](MVP-E10-S06-search-config.md)
- [MVP-E10-S07 — Bind validation configuration](MVP-E10-S07-validation-config.md)
- [MVP-E10-S08 — Bind architecture configuration](MVP-E10-S08-architecture-config.md)
- [MVP-E10-S09 — Explain effective configuration](MVP-E10-S09-explain-config.md)
- [MVP-E10-S10 — Track freshness inputs](MVP-E10-S10-freshness-inputs.md)
- [MVP-E10-S11 — Reuse disposable state](MVP-E10-S11-disposable-state.md)

## Complete when

- Invalid or unsupported configuration fails before affected execution with a
  precise correction.
- Deleting all tool-owned state changes performance only, never result
  correctness or entity-ID safety.

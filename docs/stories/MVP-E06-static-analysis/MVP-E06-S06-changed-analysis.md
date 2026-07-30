# MVP-E06-S06 — Analyze Changed Scope

## Outcome

`analyze changed` runs applicable analysis over changed files and the smallest
affected scope needed for requested evidence.

## Design

- [Static analysis](../../design/analysis-and-execution.md#static-analysis)
- [Level 3 — Dependency-aware expansion](../../design/foundations.md#level-3--dependency-aware-expansion)

## Boundary

The command does not silently expand to complete solution analysis.

## Acceptance

- Changed paths map to owning projects and affected dependents with explicit
  exclusions and confidence.
- Applicable compiler, analyzer, structural, and architecture results preserve
  their individual scopes and execution classifications.

## Verification

- Fixtures cover staged, unstaged, untracked, renamed, deleted, multi-owned,
  non-code, and public-surface changes.

## Dependencies

- `MVP-E02-S04`
- `MVP-E05-S12`
- `MVP-E06-S02`
- `MVP-E06-S03`
- `MVP-E06-S04`
- `MVP-E06-S05`

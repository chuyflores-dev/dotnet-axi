# MVP-E02-S05 — Evaluate the MSBuild Project Graph

## Outcome

Commands can obtain an evaluated project dependency graph for the selected
workspace and build properties without loading Roslyn compilations.

## Design

- [Evaluated project graph](../../design/workspace.md#evaluated-project-graph)
- [Semantic and SDK authorities](../../design/foundations.md#semantic-and-sdk-authorities)

## Boundary

The home view and commands that do not require dependency information never
trigger graph evaluation.

## Acceptance

- Evaluation honors solution, configuration, framework, and explicit MSBuild
  property selection.
- Project load failures remain represented with stable reasons.

## Verification

- MSBuild fixtures cover conditional references, central imports, cycles,
  missing assets, and evaluation failure.

## Dependencies

- `MVP-E02-S02`

# MVP-E05-S09 — Query Project Dependencies

## Outcome

`graph projects` and `graph dependencies` expose the selected evaluated
project graph through stable graph contracts.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

Project dependency edges come from evaluated MSBuild state and do not require
Roslyn compilation.

## Acceptance

- Solutions, projects, project references, package references, unsupported
  nodes, and failed evaluation remain visible.
- Direction, selected configuration/framework, and partial coverage are
  unambiguous.

## Verification

- MSBuild graph fixtures cover conditional edges, packages, unsupported
  projects, failures, and deterministic ordering.

## Dependencies

- `MVP-E05-S08`
- `MVP-E02-S05`
- `MVP-E02-S06`

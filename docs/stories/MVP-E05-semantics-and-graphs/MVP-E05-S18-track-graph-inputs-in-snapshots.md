# MVP-E05-S18 — Track graph inputs in snapshots

## Outcome

Project-graph and impact snapshots change when their evaluated project graph
changes, including changes introduced through an imported MSBuild file.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)
- [Output contract](../../design/output-contract.md)

## Boundary

Snapshots include deterministic evaluated graph provenance: project variants,
relationships, package relationships, selected properties, runtime identity,
and evaluation failures. They do not expand to unrelated workspace or runtime
state.

## Acceptance

- A graph query snapshot changes when an imported `.props` file changes an
  evaluated project relationship.
- Impact composes its semantic target snapshot with the evaluated graph
  provenance and changes for the same relationship change.
- Snapshot provenance preserves project, configuration, framework, relationship,
  and failure distinctions deterministically.

## Verification

- Focused graph and impact fixtures change an imported project reference and
  assert both the relationship result and snapshot identity change.

## Dependencies

- `MVP-E05-S08`
- `MVP-E05-S09`
- `MVP-E05-S12`

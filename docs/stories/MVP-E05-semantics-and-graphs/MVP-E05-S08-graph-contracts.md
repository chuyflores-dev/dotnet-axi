# MVP-E05-S08 — Define Graph Contracts

## Outcome

Graph services use typed nodes and edges that preserve identity, relationship
kind, scope, coverage, confidence, and provenance.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)
- [Evidence model](../../design/foundations.md#evidence-model)

## Boundary

The graph is built on demand in memory and requires no persistent graph store.

## Acceptance

- Every MVP node and edge kind can be represented without raw backend types.
- Mixed-evidence graphs retain row-level confidence where response-level
  resolution is insufficient.

## Verification

- Contract tests cover graph composition, deterministic identity, mixed
  provenance, partial coverage, and serialization.

## Dependencies

- `MVP-E01-S03`
- `MVP-E04-S02`

# MVP-E05-S08 — Define Graph Contracts

## Outcome

The 0.6 project graph uses typed project-variant nodes, observed package nodes,
and project/package-reference relationships. Rows preserve identity,
relationship kind and direction, selected configuration/framework, scope,
coverage, confidence, provenance, and incomplete evaluation without exposing
MSBuild or Roslyn backend types.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)
- [Evidence model](../../design/foundations.md#evidence-model)

## Boundary

The graph is built on demand in memory from evaluated MSBuild state and requires
no persistent graph store or Roslyn compilation. This story does not introduce
a universal node/edge framework or ship future code-entity node kinds.

## Acceptance

- Project variants and observed package references have deterministic,
  backend-independent identities.
- Project-reference and package-reference rows retain direction, selected
  configuration/framework, and typed per-row provenance and evidence.
- Unsupported, incomplete, and failed evaluation remain representable rather
  than being omitted or represented as a successful node.
- Mixed-evidence graphs retain row-level confidence where response-level
  evidence is insufficient.

## Verification

- Contract tests cover composition, deterministic identity, mixed provenance,
  partial coverage, incomplete evaluation, and serialization.

## Dependencies

- `MVP-E01-S03`
- `MVP-E04-S02`

# MVP-E06-S05 — Run Architecture Rules

## Outcome

`analyze architecture` detects configured project, namespace, layer, cycle,
and public API dependency violations.

## Design

- [Structural and architecture rules](../../design/analysis-and-execution.md#structural-and-architecture-rules)

## Boundary

The MVP evaluates the documented basic rules and does not infer undocumented
architecture conventions.

## Acceptance

- Each violation identifies the rule, source and target entities, evidence
  path, location when applicable, and evaluated scope.
- Unsupported or partially loaded projects prevent false complete results.

## Verification

- Architecture fixtures cover allowed and forbidden project/namespace edges,
  layers, cycles, public API exposure, and partial graphs.

## Dependencies

- `MVP-E06-S01`
- `MVP-E05-S09`
- `MVP-E05-S10`
- `MVP-E10-S08`

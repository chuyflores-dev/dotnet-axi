# MVP-E05-S15 - Share semantic query state within an operation

## Outcome

Reference and implementation searches reuse dependency planning and compiler-variant evaluation within one command invocation while preserving their existing target-resolution ownership, contracts, and independent CLI usefulness.

## Design

- [Semantics and graph analysis](../../design/semantics-and-graph.md)
- [Foundations for progressive analysis](../../design/foundations.md)
- [Workspace model](../../design/workspace.md)
- [Output contract](../../design/output-contract.md)

## Boundary

This story extracts one internal, operation-scoped semantic query planning session from the existing reference and implementation pipelines. It does not change Roslyn workspace ownership or compiler-context loading; those lifetime-sensitive concerns require a separate atomic story. It does not add a relationship command, change relationship-specific result models, add cross-process state, introduce a daemon or index, change the protocol, or implement callers, callees, impact, or relationship context.

## Acceptance conditions

- Each reference or implementation invocation creates one semantic query session and resolves its target through the authoritative semantic target resolver while the returned target resolution retains workspace ownership.
- The session lazily evaluates the project graph at most once and resolves compiler variants at most once for each planned project/configuration/framework combination.
- Target-owner projects evaluated during resolution are omitted from later compiler-variant evaluation misses in the same operation.
- Compiler-variant caches are isolated by the authoritative workspace root so distinct public discovery and traversal inputs preserve existing behavior.
- Reverse-dependency and target-owner plans preserve the existing deterministic project scope, test inclusion, configuration, framework, property, diagnostic, cancellation, and coverage behavior.
- Reference and implementation search retain their relationship-specific Roslyn logic, result models, stable ordering, structured errors, and output contracts.
- Regression tests establish behavioral parity and verify bounded graph and compiler-variant evaluation counts through test seams rather than timing assertions.
- Focused measurements record project evaluations, wall-clock duration, and output size before and after without changing behavior to improve the numbers.

## Verification

- Run focused reference, implementation, semantic-target, and session tests.
- Run canonical restore, Release build, and Release test commands.
- Record representative before-and-after measurements for existing reference and implementation operations.
- Complete the bounded independent-review gate.

## Dependencies

- MVP-E05-S01
- MVP-E05-S02
- MVP-E05-S03
- MVP-E05-S14

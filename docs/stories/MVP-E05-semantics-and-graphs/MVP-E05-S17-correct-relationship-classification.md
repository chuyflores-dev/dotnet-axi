# MVP-E05-S17 - Correct semantic relationship classification

## Outcome

Semantic relationship commands distinguish executable call sites from symbol-name
references, report dispatch uncertainty honestly, and reject unsupported derived
relationship targets.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)
- [Output contract](../../design/output-contract.md)

## Boundary

This story corrects caller and derived-type classification. It does not add new
relationship commands, runtime analysis, a whole-program dispatch engine, or a
new target-resolution protocol.

## Acceptance

- `callers` excludes `nameof` and other non-invocation symbol references.
- Calls whose compile-time target can dispatch to another implementation remain
  explicit about uncertainty and never claim a verified direct call.
- `derived` accepts only type targets and returns the established unsupported
  target correction for members.
- Focused regressions cover abstract dispatch, overridable dispatch, `nameof`,
  and a method passed to derived-type search.

## Verification

- Run focused caller and derived-type regressions.
- Run canonical restore, Release build, and Release test commands.
- Complete the bounded independent-review gate.

## Dependencies

- MVP-E05-S01
- MVP-E05-S05
- MVP-E05-S06

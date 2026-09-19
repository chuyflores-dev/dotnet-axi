# MVP-E13-S46 — Add Virtual-override Semantic Benchmark Task

## Outcome

The manual agent benchmark adds one neutral repository change that requires
renaming a virtual member and its exact compiler overrides while preserving a
hidden same-named member and virtual-dispatch behavior.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the virtual-override corpus increment. It does not change
shipped commands, recreate prior generic or hidden-member type cases, add
callers, graph or impact cases, dispatch a paid run, add scheduling or
reconciliation infrastructure, or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-project, multi-targeted
  fixture, neutral prompt, allowed paths, timeout, and hidden validator.
- The task requires renaming one specified virtual member and every exact
  compiler override and virtual dispatch call site that resolves to it.
- The fixture includes a same-named `new` hidden member; it and its observable
  behavior remain unchanged.
- The task declares baseline and candidate applicability. The materialized
  fixture has a deterministic input hash recorded by the harness.
- The hidden oracle is absent during the agent turn, rejects the untouched
  state, an incomplete override or dispatch update, retained compatibility
  members, a changed hidden member, and changed behavior, and accepts only the
  permitted scoped change.
- Harness self-tests list and validate the task without dispatching an agent.

## Verification

- Known original, incomplete override, incomplete dispatch, exact-change,
  retained-member, hidden-member, and changed-behavior fixture states prove
  the hidden oracle.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E13-S41`
- `MVP-E13-S42`
- `MVP-E13-S43`
- `MVP-E13-S44`
- `MVP-E13-S45`
- `MVP-E05-S01`
- `MVP-E05-S04`

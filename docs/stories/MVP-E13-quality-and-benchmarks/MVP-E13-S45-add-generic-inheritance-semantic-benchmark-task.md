# MVP-E13-S45 — Add Generic-inheritance Semantic Benchmark Task

## Outcome

The manual agent benchmark adds one neutral repository change that requires
renaming a generic base type across constructed inheritance and consumer type
references without changing a hidden same-named member in one descendant.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the generic-inheritance corpus increment. It does not
change shipped commands, recreate the prior contract-member or unrelated-name
cases, add overrides, graph or impact cases, dispatch a paid run, add
scheduling or reconciliation infrastructure, or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-project, multi-targeted
  fixture, neutral prompt, allowed paths, timeout, and hidden validator.
- The task requires renaming one specified generic base type and every
  production inheritance and consumer type reference that resolves to it.
- The fixture includes several constructed generic descendants plus a descendant
  with a `new` same-named member; that hidden member and every selected
  observable output remain unchanged.
- The task declares baseline and candidate applicability. The materialized
  fixture has a deterministic input hash recorded by the harness.
- The hidden oracle is absent during the agent turn, rejects the untouched
  state, retained old generic type, a changed hidden member, and changed
  behavior, and accepts only the permitted scoped change.
- Harness self-tests list and validate the task without dispatching an agent.

## Verification

- Known original, exact-change, retained-type, hidden-member, and
  changed-behavior fixture states prove the hidden oracle.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E13-S41`
- `MVP-E13-S42`
- `MVP-E13-S43`
- `MVP-E13-S44`
- `MVP-E05-S01`
- `MVP-E05-S05`

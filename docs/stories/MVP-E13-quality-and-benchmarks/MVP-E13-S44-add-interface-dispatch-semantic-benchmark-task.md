# MVP-E13-S44 — Add Interface-dispatch Semantic Benchmark Task

## Outcome

The manual agent benchmark adds one neutral repository change that requires
updating a contract member, each of several implementations, and only the
interface-typed production consumers that dispatch through that contract.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the interface-dispatch corpus increment. It does not
change shipped commands, recreate the prior owner, overload, or unrelated-name
cases, add overrides, graph or impact cases, dispatch a paid run, add
scheduling or reconciliation infrastructure, or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-project, multi-targeted
  fixture, neutral prompt, allowed paths, timeout, and hidden validator.
- The task requires renaming one specified interface member, each production
  implementation, and each interface-typed production dispatch site while
  preserving the distinct observable outputs selected by every implementation.
- The fixture includes several implementations and a consumer that selects an
  implementation only through the interface type; it has no unrelated owner,
  concrete-only consumer, or overload decoy covered by earlier corpus tasks.
- The task declares baseline and candidate applicability. The materialized
  fixture has a deterministic input hash recorded by the harness.
- The hidden oracle is absent during the agent turn, rejects the untouched
  state, an incomplete implementation or dispatch update, retained string
  compatibility members, and changed behavior, and accepts only the permitted
  scoped change.
- Harness self-tests list and validate the task without dispatching an agent.

## Verification

- Known original, incomplete implementation, incomplete dispatch, exact-change,
  retained-member, and changed-behavior fixture states prove the hidden oracle.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E13-S41`
- `MVP-E13-S42`
- `MVP-E13-S43`
- `MVP-E05-S01`
- `MVP-E05-S02`
- `MVP-E05-S03`

# MVP-E13-S42 — Add Exact-overload Semantic Benchmark Task

## Outcome

The manual agent benchmark adds one neutral cross-project repository change
that requires selecting an exact overload and tracing its compiler-verified
references and implementation using only capabilities already shipped on
`main`.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the exact-overload corpus increment. It does not change
the shipped commands, recreate the existing interface-member rename task,
add graph or impact cases, dispatch a paid run, add scheduling or
reconciliation infrastructure, or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-project, multi-targeted
  fixture, neutral prompt, allowed paths, timeout, and hidden validator.
- The task requires renaming only the `Format(string)` interface member,
  implementations, and call sites while preserving a same-named `Format(int)`
  overload and all observable behavior.
- The task declares baseline and candidate applicability. The materialized
  fixture has a deterministic input hash recorded by the harness.
- The hidden oracle is absent during the agent turn, rejects the untouched
  state, a wrong-overload change, retained string compatibility members, and
  changed behavior, and accepts only the permitted scoped change.
- Harness self-tests list and validate the task without dispatching an agent.

## Verification

- Known original, exact-change, wrong-overload, retained-member, and
  changed-behavior fixture states prove the hidden oracle.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E13-S41`
- `MVP-E05-S01`
- `MVP-E05-S02`
- `MVP-E05-S03`

# MVP-E13-S47 — Add Extension-method Semantic Benchmark Task

## Outcome

The manual agent benchmark adds one neutral repository change that requires
renaming an extension member and its extension call site while preserving
same-named instance and static members.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the extension-method corpus increment. It does not change
shipped commands, recreate prior overload or virtual-dispatch cases, add graph
or impact cases, dispatch a paid run, add scheduling or reconciliation
infrastructure, or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-project, multi-targeted
  fixture, neutral prompt, allowed paths, timeout, and hidden validator.
- The task requires renaming one specified extension `Format(string)` member
  and the call site bound to it.
- The fixture includes same-named instance and static methods; their signatures
  and observable behavior remain unchanged.
- The hidden oracle rejects untouched, wrong-target, retained compatibility,
  and changed-behavior states, and accepts only the permitted scoped change.
- Harness self-tests list and validate the task without dispatching an agent.

## Verification

- Known original, exact-change, wrong-target, retained-member, and
  changed-behavior fixture states prove the hidden oracle.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E13-S41`
- `MVP-E13-S42`
- `MVP-E13-S43`
- `MVP-E13-S46`
- `MVP-E05-S01`
- `MVP-E05-S07`

# MVP-E13-S48 — Add Partial-declaration Linked-ownership Semantic Benchmark Task

## Outcome

The manual agent benchmark adds one neutral repository change that requires
renaming a member declared in a partial type whose source file is linked into
two independent project owners.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the partial-declaration and linked-ownership corpus
increment. It does not change shipped commands, recreate prior overload or
extension-method cases, add multi-target conditional-compilation or impact
cases, dispatch a paid run, add scheduling or reconciliation infrastructure,
or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-project fixture, neutral
  prompt, allowed paths, timeout, and hidden validator.
- The task requires renaming one specified `Format(string)` member declared in
  a partial type and every production call site owned by the projects that link
  its physical source file.
- The fixture preserves the linked source path, both project owners, the
  partial type, and the distinct `Format(int)` overload and behavior.
- The hidden oracle rejects untouched, wrong-target, incomplete-owner,
  retained-compatibility, bypassed-call, and changed-behavior states, and
  accepts only the permitted scoped change.
- Harness self-tests list and validate the task without dispatching an agent.

## Verification

- Known original, exact-change, wrong-target, incomplete-owner,
  retained-member, bypassed-call, and changed-behavior fixture states prove the
  hidden oracle.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E13-S41`
- `MVP-E05-S01`
- `MVP-E05-S02`

# MVP-E13-S43 — Add Unrelated-name Semantic Benchmark Task

## Outcome

The manual agent benchmark adds one neutral repository change that requires
selecting a contract member across its references and implementation without
changing identically named declarations in an unrelated project.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the unrelated-name corpus increment. It does not change
the shipped commands, recreate prior owner or overload cases, add graph or
impact cases, dispatch a paid run, add scheduling or reconciliation
infrastructure, or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-project, multi-targeted
  fixture, neutral prompt, allowed paths, timeout, and hidden validator.
- The task requires renaming the specified contract `Format(string)` member,
  implementation, and call site while preserving same-named interface,
  implementation, and call paths in an unrelated project.
- The task declares baseline and candidate applicability. The materialized
  fixture has a deterministic input hash recorded by the harness.
- The hidden oracle is absent during the agent turn, rejects the untouched
  state, a wrong-target or unrelated-project change, retained string
  compatibility members, and changed behavior, and accepts only the
  permitted scoped change.
- Harness self-tests list and validate the task without dispatching an agent.

## Verification

- Known original, exact-change, wrong-target, unrelated-project,
  retained-member, and changed-behavior fixture states prove the hidden
  oracle.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E13-S41`
- `MVP-E13-S42`
- `MVP-E05-S01`
- `MVP-E05-S02`
- `MVP-E05-S03`

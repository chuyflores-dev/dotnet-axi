# MVP-E13-S50 — Add Impact Semantic Benchmark Task

## Outcome

The manual benchmark adds one neutral public-member rename that crosses
production and candidate-test projects selected by the shipped impact contract.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

This story adds only the impact corpus increment. It does not change the
shipped impact command, dispatch a paid run, or use a model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed three-project fixture, neutral
  prompt, allowed paths, timeout, and hidden validator.
- The task renames one public `Format(string)` member and its production and
  candidate-test call sites while retaining the `Format(int)` overload.
- The fixture contains an affected candidate-test project with evaluated xUnit
  package evidence, matching the shipped impact selection contract.
- The hidden oracle rejects untouched, incomplete, retained-member,
  wrong-target, bypassed/ignored/reproduced-result call-site, and
  changed-behavior states.

## Dependencies

- `MVP-E13-S41`
- `MVP-E05-S12`

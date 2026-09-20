# MVP-E13-S49 — Add Multi-target Conditional Semantic Benchmark Task

## Outcome

The manual benchmark adds one neutral rename that must preserve distinct
conditional-compilation meanings across every target framework.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story adds only the multi-target conditional corpus increment. It does not
change shipped commands, add impact cases, dispatch a paid run, or use a
model-judged oracle.

## Acceptance

- Baseline and candidate receive the same fixed multi-target fixture, neutral
  prompt, allowed paths, timeout, and hidden validator.
- The task renames one `Format(string)` member and its call site in every
  compiled target meaning.
- The fixture retains net8.0 and net10.0 conditional behaviors and validates
  both frameworks independently.
- The hidden oracle rejects untouched, incomplete-framework, retained-member,
  wrong-target, and changed-behavior states.

## Dependencies

- `MVP-E13-S41`
- `MVP-E05-S01`

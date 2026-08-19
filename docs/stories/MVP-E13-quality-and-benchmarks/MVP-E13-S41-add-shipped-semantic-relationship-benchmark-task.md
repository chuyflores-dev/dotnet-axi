# MVP-E13-S41 — Add Shipped Semantic-relationship Benchmark Task

## Outcome

The manual agent benchmark adds one neutral cross-project repository change
that requires exact semantic target selection, references, and implementations
using only capabilities already shipped on `main`.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

This story does not complete the broader S19 corpus. It adds no overrides,
derived types, callers, callees, project graph, impact, relationship context,
analysis, validation product command, paid run, repetition scheduler, provider
reconciliation, or model-judged oracle. Diagnostic metrics do not change the
existing correctness and scope gates.

## Acceptance

- Baseline and candidate receive the same fixed three-project fixture, prompt,
  allowed paths, timeout, and hidden validator; the prompt names no tool or
  command sequence.
- The task requires renaming one interface member across its declaration, two
  implementations, one interface-typed call site, and one concrete-typed call
  site while preserving distinct observable behavior and both target
  frameworks.
- The hidden oracle is absent during the agent turn, rejects the untouched
  state, retained compatibility members, and changed behavior, and accepts the
  exact scoped change.
- Results retain a deterministic fixture hash, fixed-classifier raw repository
  read command count, marker-derived semantic-oracle outcome, and nullable
  recovered-`dnaxi` failure evidence.
- Raw-read classification is diagnostic-only, counts unique command IDs, and
  is compared only within the same host/provider environment. Final prose and
  inferred intent are never graded or parsed.

## Verification

- Harness tests list and validate the third task without dispatching an agent.
- Known original, correct, retained-member, and changed-behavior fixtures prove
  the hidden oracle.
- Synthetic events prove read-command deduplication and direct completed
  success/nonzero `dnaxi` classification.
- Canonical restore, build, and test verification passes without a paid run.

## Dependencies

- `MVP-E09-S18`
- `MVP-E13-S40`
- `MVP-E05-S01`
- `MVP-E05-S02`
- `MVP-E05-S03`

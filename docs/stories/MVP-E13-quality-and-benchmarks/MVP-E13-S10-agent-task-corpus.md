# MVP-E13-S10 — Define the Agent-task Corpus

## Outcome

A controlled task corpus represents the documented .NET discovery, semantic,
diagnostic, validation, and targeted-change scenarios.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Mutation scenarios enter the MVP gate only when the release actually contains
the corresponding supported mutation feature.

## Acceptance

- Each task has fixed repository state, success and safety oracles, permitted
  tools, timeout, required validation, and baseline/candidate applicability.
- The corpus covers every MVP scenario category without embedding
  condition-specific hidden guidance.

## Verification

- Corpus validation detects ambiguous outcomes, leaked candidate guidance,
  invalid fixtures, missing validation rules, and nondeterministic setup.

## Dependencies

- `MVP-E13-S01`
- `MVP-E13-S02`
- `MVP-E13-S03`

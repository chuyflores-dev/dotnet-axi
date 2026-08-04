# MVP-E13-S10 — Define the Agent-task Corpus

## Outcome

A controlled initial task corpus represents the documented .NET
source-discovery scenarios.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Semantic, impact, diagnostic, validation, and mutation scenarios enter through
separate milestone stories only when the release contains their corresponding
supported capability.

## Acceptance

- Each task has fixed repository state, success and safety oracles, permitted
  tools, timeout, required validation, and baseline/candidate applicability.
- The corpus covers applicable file, text, structural, and stable syntax
  discovery without condition-specific hidden guidance.
- The schema supports later milestone extensions without treating unshipped
  tasks as missing 0.3.0 evidence.
- Deterministic oracles are used where possible; any model-judged criterion is
  explicit, blinded to the condition, and independently versioned.

## Verification

- Corpus validation detects ambiguous outcomes, leaked candidate guidance,
  invalid fixtures, missing validation rules, and nondeterministic setup.

## Dependencies

- `MVP-E13-S01`
- `MVP-E13-S02`
- `MVP-E13-S03`

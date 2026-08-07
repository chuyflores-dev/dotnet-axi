# MVP-E09-S17 — Teach Configuration and Validation in the Agent Skill

## Outcome

The released Agent Skill teaches agents to use repository configuration,
freshness evidence, and deterministic fast or standard validation without
overstating completion.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)
- [Freshness and cache](../../design/runtime-and-distribution.md#freshness-and-cache)
- [Validation](../../design/analysis-and-execution.md#validation)

## Boundary

Guidance does not select the post-MVP full profile, turn validation into a
source-writing operation, assume retained state is authoritative, or teach
agent setup and repair before their milestone.

## Acceptance

- Guidance explains root configuration discovery, schema errors, relative and
  external paths, CLI/repository/derived/default precedence, and passive
  `--explain-plan` output without exposing secret-bearing values.
- Agents use fast validation during work and standard validation before a
  completion claim when applicable, after reviewing the resolved checks and
  their repository-code, network, and artifact effects.
- Guidance preserves affected-scope evidence, candidate-test uncertainty,
  zero-test policy, unavailable and skipped checks, partial coverage, child
  failures, cancellation, timeouts, and protected diagnostic artifacts.
- Uncommitted and untracked inputs remain part of freshness; agents never
  treat modification time or tool-owned state as correctness authority, and
  state deletion may affect performance only.
- The invoked version's help and capabilities remain authoritative, and
  committed, packaged, structured-help, and home-view guidance stay generated
  from one source and byte-consistent where required.

## Verification

- Golden generation and packaged-skill tests cover configuration correction,
  precedence explanation, freshness changes, profile selection, effect
  preflight, affected scope, pass/fail/partial/zero-test/cancelled verdicts,
  artifact handling, source-write rejection, and absence of deferred setup or
  full-profile guidance.

## Dependencies

- `MVP-E09-S16`
- `MVP-E07-S01`
- `MVP-E07-S02`
- `MVP-E07-S03`
- `MVP-E07-S04`
- `MVP-E07-S05`
- `MVP-E07-S06`
- `MVP-E07-S07`
- `MVP-E07-S08`
- `MVP-E10-S07`
- `MVP-E10-S09`
- `MVP-E10-S10`
- `MVP-E10-S11`

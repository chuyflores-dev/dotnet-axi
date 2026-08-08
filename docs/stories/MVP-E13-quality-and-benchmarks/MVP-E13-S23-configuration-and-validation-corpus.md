# MVP-E13-S23 — Add 0.8.0 Configuration and Validation Tasks

## Outcome

The agent-task corpus adds deterministic repository-configuration,
freshness, affected-scope, and validation scenarios for 0.8.0.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)
- [Freshness and cache](../../design/runtime-and-distribution.md#freshness-and-cache)
- [Validation](../../design/analysis-and-execution.md#validation)

## Boundary

The corpus does not add full validation, package or vulnerability policy,
agent setup, daemon, or source-modification tasks before those capabilities
ship.

## Acceptance

- Configuration tasks cover discovery, invalid schema and keys, path safety,
  precedence, named-profile expansion, unknown or cyclic checks, test policy,
  redacted plan explanation, and actionable correction.
- Freshness tasks mutate committed, uncommitted, untracked, project, import,
  configuration, asset, editor, SDK, and adapter inputs and compare reusable,
  invalidated, fresh-process, and state-deleted results.
- Validation tasks cover affected scope, fast and standard profiles, effect
  preflight, ordered checks, both supported test platforms, zero-test policy,
  fail-fast and continuation, cancellation, timeout, summary, and protected
  evidence.
- Safety oracles reject source writes, hidden repository-code or network
  execution, stale-state authority, false success from zero tests or partial
  scope, secret disclosure, and unsupported completion claims.
- Each task declares raw-tool and candidate applicability, fixed state,
  deterministic success and safety oracles, timeout, permitted effects, and
  required validation evidence.

## Verification

- Known valid, invalid, stale, partial, unavailable, passing, failing,
  zero-test, cancelled, timed-out, redacted, and state-deleted fixtures prove
  the task oracles independently of a paid agent run.

## Dependencies

- `MVP-E13-S10`
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
- `MVP-E09-S17`

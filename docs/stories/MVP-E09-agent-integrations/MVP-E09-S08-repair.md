# MVP-E09-S08 — Repair Integrations

## Outcome

Setup can repair stale or incomplete tool-owned integration state.

## Design

- [Explicit setup](../../design/agent-integration.md#explicit-setup)
- [Path and configuration safety](../../design/agent-integration.md#path-and-configuration-safety)

## Boundary

Repair preserves unrelated configuration, hooks, skills, backups, trust state,
and managed policy.

## Acceptance

- Moved invocations, outdated generated guidance, partial installs, and
  duplicate tool-owned entries produce deterministic repair.
- Repeating repair against a correct integration is a no-op and reports the
  exact retained targets.

## Verification

- Lifecycle fixtures cover clean, moved, outdated, partial, duplicate,
  concurrent, customized, backed-up, and repeated repair states.

## Dependencies

- `MVP-E09-S03`
- `MVP-E09-S04`
- `MVP-E09-S05`
- `MVP-E09-S07`

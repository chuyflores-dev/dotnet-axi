# MVP-E13-S25 — Add 0.8.0 Safe Agent-integration Tasks

## Outcome

The agent-task corpus adds deterministic setup, repair, removal, passive-hook,
artifact-lifecycle, and constrained-host scenarios for 0.8.0.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Explicit setup](../../design/agent-integration.md#explicit-setup)
- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)
- [Security and privacy](../../design/runtime-and-distribution.md#security-and-privacy)

## Boundary

The corpus does not add OpenCode installation, the full cross-platform
restricted-host matrix, general source modification, or full dual-agent
release-gate tasks before their corresponding capabilities ship.

## Acceptance

- Setup tasks cover repository and user scopes, invocation resolution,
  structural configuration merge, atomic backup, Claude Code and Codex
  adapters, trust or managed-policy blockers, and unsupported formats.
- Lifecycle tasks cover idempotent install, repair, and removal while
  preserving unrelated configuration, hooks, skills, trust state, policy,
  permissions, backups, and the unselected scope.
- Passive-hook tasks prove bounded directory-scoped context, no transcript or
  prompt access, and no restore, analyzer, generator, repository-code, or
  network effects.
- Safety tasks cover write-substitution defense, artifact retention and
  cleanup, constrained-host classification, no-loop recovery, and structured
  OpenCode not-supported results with no partial writes.
- Deterministic success and safety oracles distinguish authorized setup writes
  from source writes, reject hidden permission broadening or stale workers,
  and verify exact changed and retained targets.
- Mutation-capable tasks use only fixture-owned repository and user roots and
  declare workspace-write permission explicitly; passive tasks remain
  read-only.

## Verification

- Known clean, existing, malformed, protected, linked, moved, partial,
  duplicate, policy-disabled, permission-denied, expired-artifact, cancelled,
  and repeated fixtures prove the task oracles independently of a paid agent
  run.

## Dependencies

- `MVP-E13-S10`
- `MVP-E09-S01`
- `MVP-E09-S02`
- `MVP-E09-S03`
- `MVP-E09-S04`
- `MVP-E09-S05`
- `MVP-E09-S06`
- `MVP-E09-S07`
- `MVP-E09-S08`
- `MVP-E09-S09`
- `MVP-E09-S10`
- `MVP-E11-S07`
- `MVP-E11-S09`
- `MVP-E11-S10`
- `MVP-E11-S11`

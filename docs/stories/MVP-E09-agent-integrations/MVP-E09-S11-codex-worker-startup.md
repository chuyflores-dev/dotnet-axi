# MVP-E09-S11 — Guide Safe Codex Worker Startup

## Outcome

Generated Codex guidance routes clean-context work through an explicit startup
and cleanup boundary without duplicate launches or stale polling.

## Design

- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)

## Boundary

This story updates generated Codex guidance and its deterministic checks.
It does not implement the Codex benchmark adapter, general constrained-host
failure contracts, or the cross-platform restricted-host matrix.

## Acceptance

- Codex guidance prefers native subagents for clean-context delegation because
  they inherit the parent turn's sandbox and live approval overrides.
- A standalone `codex exec` starts only from an owning host or automation
  boundary already permitted to initialize Codex and access the selected
  worktree; it is not launched as a sandboxed child to escape that boundary.
- A denial before `thread.started` means no Codex thread identity was observed;
  it does not prove that the launcher process exited. Guidance preserves the
  diagnostic and stops polling an event stream that never began.
- Before any replacement, guidance observes the exact launcher process and
  confirms exit or terminates, waits for, and reaps that child. Event absence
  or silence never authorizes a duplicate, and retry still requires a confirmed
  boundary change.
- Portable Agent Skill guidance remains agent-neutral and contains no Codex
  worker flags or host-specific recovery procedure.

## Verification

- Generated-document tests enforce the worker-boundary, startup-denial, and
  portability contracts.
- The committed Agent Skill documents match their canonical generator output.

## Dependencies

- `MVP-E09-S07`

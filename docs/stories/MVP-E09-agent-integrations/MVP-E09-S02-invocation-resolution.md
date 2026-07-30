# MVP-E09-S02 — Resolve a Stable Invocation

## Outcome

Setup selects an invocation that remains valid for the current global or local
tool installation and can be repaired after it moves.

## Design

- [Path and configuration safety](../../design/agent-integration.md#path-and-configuration-safety)
- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)

## Boundary

PATH resolution is preferred only when it identifies the intended executable;
otherwise setup uses an explicit supported path or local-tool invocation.

## Acceptance

- Resolution records why PATH, absolute, or local-tool invocation was chosen.
- Stale, shadowed, moved, and missing installations produce a safe repair plan.

## Verification

- Executable fixtures cover global, local, shadowed, moved, missing, relative,
  and platform-specific paths.

## Dependencies

- `MVP-E09-S01`
- `MVP-E08-S01`

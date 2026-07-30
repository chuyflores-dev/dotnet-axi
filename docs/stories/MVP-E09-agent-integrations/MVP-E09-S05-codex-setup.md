# MVP-E09-S05 — Set Up Codex

## Outcome

`setup codex` installs the supported repository or user integration while
respecting Codex trust and policy behavior.

## Design

- [Codex hooks](../../design/agent-integration.md#codex-hooks)
- [Explicit setup](../../design/agent-integration.md#explicit-setup)

## Boundary

Setup never bypasses trust review, enables a managed-disabled hook, or claims a
hook can run when policy prevents it.

## Acceptance

- Repository and user scopes select one supported hook representation per
  layer and preserve additive sources.
- Trust-review and disabled-policy states are reported accurately.

## Verification

- Adapter fixtures cover hooks JSON, config TOML, additive layers, trust
  review, managed disablement, duplicate setup, and unsupported formats.

## Dependencies

- `MVP-E09-S02`
- `MVP-E09-S03`

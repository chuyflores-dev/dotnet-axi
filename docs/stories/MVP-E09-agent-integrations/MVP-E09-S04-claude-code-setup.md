# MVP-E09-S04 — Set Up Claude Code

## Outcome

`setup claude-code` installs the supported repository or user integration
without changing unrelated Claude Code configuration.

## Design

- [Explicit setup](../../design/agent-integration.md#explicit-setup)
- [Session-start context](../../design/agent-integration.md#session-start-context)

## Boundary

The adapter supports only documented Claude Code configuration formats and
does not infer future formats.

## Acceptance

- Both scopes install the correct session-start invocation and report every
  planned or applied file change.
- Repeating setup is a no-op when already correct.

## Verification

- Adapter fixtures cover repository/user scope, existing hooks, duplicate
  setup, unsupported formats, path spaces, repair-needed state, and removal
  handoff.

## Dependencies

- `MVP-E09-S02`
- `MVP-E09-S03`

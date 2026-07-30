# MVP-E09-S06 — Emit Session-start Context

## Outcome

Installed hooks emit a passive directory-scoped home summary of at most 1,000
characters.

## Design

- [Session-start context](../../design/agent-integration.md#session-start-context)

## Boundary

Hooks consume no prompt text, transcript path, session ID, or unrelated event
payload and never read agent transcripts.

## Acceptance

- Context describes only the current workspace and is smaller than an ordinary
  explicit query response.
- Hook execution performs no restore, analyzer, generator, project graph,
  repository-code execution, or network access.

## Verification

- Hook fixtures run from repository subdirectories with oversized workspaces,
  hostile payload fields, missing tools, and passive-operation monitors.

## Dependencies

- `MVP-E01-S07`
- `MVP-E09-S04`
- `MVP-E09-S05`
- `MVP-E11-S02`

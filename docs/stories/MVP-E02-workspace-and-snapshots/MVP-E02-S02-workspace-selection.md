# MVP-E02-S02 — Select the Workspace Entry Point

## Outcome

Workspace commands select one solution or project using explicit selectors,
configuration, and deterministic fallback precedence.

## Design

- [Solution and project selection](../../design/workspace.md#solution-and-project-selection)

## Boundary

Ambiguity is a structured usage error and never triggers an interactive
prompt or arbitrary selection.

## Acceptance

- `--solution`, `--project`, configuration, and single-root-candidate
  precedence is enforced.
- Ambiguous and invalid selections list candidates and a concrete correction.

## Verification

- Selection tests cover every precedence level, name/path matching, and
  ambiguity.

## Dependencies

- `MVP-E02-S01`
- `MVP-E01-S05`

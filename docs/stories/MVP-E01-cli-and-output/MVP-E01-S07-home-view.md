# MVP-E01-S07 — Render the Home View

## Outcome

Running the CLI without arguments returns a compact passive view of the current
workspace.

## Design

- [Home, help, and suggestions](../../design/output-contract.md#home-help-and-suggestions)
- [Level 0 — Repository catalog](../../design/foundations.md#level-0--repository-catalog)

## Boundary

The home view performs no project-graph evaluation, compilation, restore,
analyzer execution, or network access.

## Acceptance

- Output includes the documented workspace, Git, cheap-count, and analysis
  state fields.
- Expensive or unavailable state is labeled unknown or not loaded.

## Verification

- Integration tests cover Git, non-Git, ambiguous, and empty directories while
  proving no executing dependency starts.

## Dependencies

- `MVP-E01-S05`
- `MVP-E02-S01`
- `MVP-E02-S03`

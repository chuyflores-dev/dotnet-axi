# MVP-E01-S12 — Recommend Only Available Commands

## Outcome

The 0.2.0 home view recommends only commands or root options exposed by the
installed CLI version.

## Design

- [Home, help, and suggestions](../../design/output-contract.md#home-help-and-suggestions)

## Boundary

The shared suggestion composer remains available for later capability stories.
This story does not add the planned search, analysis, or validation commands.

## Acceptance

- A home suggestion resolves through the active parser instead of advertising
  a planned but unavailable command.
- With no shipped capability subcommands, home emits only `dnaxi --help` while
  preserving its passive workspace and worktree summary.
- Help, version, Agent Skill guidance, and structured output contracts remain
  unchanged.

## Verification

- Home integration tests cover Git, non-Git, ambiguous, and empty workspaces
  and reject `search`, `analyze`, and `validate` suggestions.
- The suggested root help option succeeds without creating home dependencies.

## Dependencies

- `MVP-E01-S07`
- `MVP-E01-S08`
- `MVP-E01-S11`

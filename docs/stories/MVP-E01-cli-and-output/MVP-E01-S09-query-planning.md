# MVP-E01-S09 — Explain Query Plans

## Outcome

Commands can select the least expensive capable engine and report the planned
scope through `--explain-plan`.

## Design

- [Query planning](../../design/output-contract.md#query-planning)
- [Progressive analysis](../../design/foundations.md#progressive-analysis)

## Boundary

Each capability supplies its own engine choices; this story supplies the
shared plan contract and selection mechanism.

## Acceptance

- Plans describe engine class, candidate scope, expected project loads, and
  whether complete analysis is required.
- Fixed workspace selectors are preserved in plan output and follow-up
  commands.

## Verification

- Planner tests use fake engines to prove least-cost selection and explicit
  complete-scope escalation.

## Dependencies

- `MVP-E01-S02`
- `MVP-E01-S03`

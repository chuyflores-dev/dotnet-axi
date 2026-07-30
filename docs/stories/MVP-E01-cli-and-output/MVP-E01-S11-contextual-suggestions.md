# MVP-E01-S11 — Suggest Contextual Follow-ups

## Outcome

Command results can include a few complete, scope-preserving follow-up commands
when another step is predictably useful.

## Design

- [Home, help, and suggestions](../../design/output-contract.md#home-help-and-suggestions)

## Boundary

Suggestions use templates or explicit placeholders and are omitted when the
response is already self-contained.

## Acceptance

- Suggestions preserve fixed solution, project, configuration, and framework
  selectors.
- Runtime values are never invented and suggestions remain deterministic and
  bounded.

## Verification

- Golden tests cover home, empty, ambiguous, partial, error, self-contained,
  and fixed-scope responses.

## Dependencies

- `MVP-E01-S03`
- `MVP-E01-S06`

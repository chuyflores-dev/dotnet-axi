# MVP-E01 — CLI Foundation and Output Contract

## Outcome

A runnable command host exposes deterministic, noninteractive, typed results
through strict TOON v4.1 output schema `dotnet-axi/v1`.

## Scope

- Solution and component scaffold for the CLI and stable internal contracts.
- Command dispatch, query-planning seams, home, help, and version behavior.
- Evidence envelopes, errors, suggestions, deterministic ordering, and output
  budgets.
- Stdout/stderr separation and the public `0/1/2` exit contract.

## Boundary

Capability-specific commands and release packaging belong to their respective
epics.

## Design

- [Design foundations](../../design/foundations.md)
- [CLI and output contract](../../design/output-contract.md)
- [Runtime and distribution](../../design/runtime-and-distribution.md)

## Dependencies

None.

## Stories

- [MVP-E01-S01 — Scaffold the solution](MVP-E01-S01-solution-scaffold.md)
- [MVP-E01-S02 — Create the command host](MVP-E01-S02-command-host.md)
- [MVP-E01-S03 — Define evidence contracts](MVP-E01-S03-evidence-contracts.md)
- [MVP-E01-S04 — Serialize TOON output](MVP-E01-S04-toon-serialization.md)
- [MVP-E01-S05 — Enforce the CLI response boundary](MVP-E01-S05-cli-response-boundary.md)
- [MVP-E01-S06 — Shape bounded output](MVP-E01-S06-output-shaping.md)
- [MVP-E01-S07 — Render the home view](MVP-E01-S07-home-view.md)
- [MVP-E01-S08 — Generate command help](MVP-E01-S08-command-help.md)
- [MVP-E01-S09 — Explain query plans](MVP-E01-S09-query-planning.md)
- [MVP-E01-S10 — Report the CLI version](MVP-E01-S10-version-output.md)
- [MVP-E01-S11 — Suggest contextual follow-ups](MVP-E01-S11-contextual-suggestions.md)
- [MVP-E01-S12 — Recommend only available commands](MVP-E01-S12-available-home-suggestions.md)

## Complete when

- Representative home, help, success, empty, partial, and error results
  strict-decode under the pinned TOON contract.
- Other capability epics can add handlers without exposing backend-specific
  types or changing the shared output and exit behavior.

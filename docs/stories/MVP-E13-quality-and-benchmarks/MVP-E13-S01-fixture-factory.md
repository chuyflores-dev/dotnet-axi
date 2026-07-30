# MVP-E13-S01 — Build the Integration Fixture Factory

## Outcome

Tests can create isolated deterministic repositories from committed manifests,
templates, and fixed seeds.

## Design

- [Integration fixtures](../../design/quality.md#integration-fixtures)

## Boundary

The factory creates test inputs and records toolchain identity; it does not
encode product assertions.

## Acceptance

- Fixtures have stable content hashes, isolated Git/config/cache/artifact
  state, selected SDK context, and deterministic cleanup.
- Tests can opt into restore or executing components without making passive
  fixtures perform network access.

## Verification

- Self-tests create equivalent fixtures repeatedly and prove isolation across
  concurrent runs and failed cleanup.

## Dependencies

- `MVP-E01-S01`

# MVP-E13-S07 — Run Security Adversarial Tests

## Outcome

A repeatable suite proves the passive/executing boundary and documented
process, path, output, secret, artifact, and setup protections.

## Design

- [Security](../../design/quality.md#security)

## Boundary

The suite verifies documented controls and does not claim the product or
executed repository code is an operating-system sandbox.

## Acceptance

- Tests cover network, restore, telemetry, shell metacharacters, malicious
  paths, symlink substitution, output injection, secrets, permissions,
  retention, and source-write gating.
- Entity IDs also prove fresh-process resolution and safe stale failure after
  state deletion.

## Verification

- Security tests run in isolated monitored fixtures and fail on any undeclared
  side effect or leaked sentinel.

## Dependencies

- `MVP-E11-S02`
- `MVP-E11-S04`
- `MVP-E11-S05`
- `MVP-E11-S06`
- `MVP-E11-S07`
- `MVP-E11-S08`
- `MVP-E11-S09`
- `MVP-E11-S10`
- `MVP-E04-S03`

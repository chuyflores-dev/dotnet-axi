# MVP-E11-S09 — Clean Retained Artifacts

## Outcome

An explicit cleanup operation removes expired tool-owned diagnostic artifacts
according to the documented retention policy.

## Design

- [Diagnostic artifacts](../../design/runtime-and-distribution.md#diagnostic-artifacts)

## Boundary

Cleanup never follows links or removes unverified directories, repository
files, user configuration, or active artifacts.

## Acceptance

- Default seven-day and explicit supported retention selection identify exact
  eligible targets before deletion.
- Results report removed, retained, active, unsafe, and failed targets without
  broad recursive assumptions.

## Verification

- Time-controlled fixtures cover expired, recent, active, foreign, linked,
  malformed, permission-denied, and repeated cleanup.

## Dependencies

- `MVP-E11-S06`
- `MVP-E11-S07`

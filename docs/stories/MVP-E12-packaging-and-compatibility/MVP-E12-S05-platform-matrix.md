# MVP-E12-S05 — Publish the Platform Matrix

## Outcome

The packaged CLI is built and exercised on the current supported Windows,
macOS, and Linux runner matrix.

## Design

- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)
- [Output and platform](../../design/quality.md#output-and-platform)

## Boundary

The published matrix claims only the exact OS and RID combinations exercised
for the release.

## Acceptance

- Package installation, paths, LF-only output, process-tree cancellation,
  executable discovery, setup editing, and supported permissions run on each
  matrix entry.
- Platform-specific limitations are explicit release evidence.

## Verification

- The CI matrix produces retained structured results for every declared OS/RID
  combination.

## Dependencies

- `MVP-E12-S02`
- `MVP-E11-S04`
- `MVP-E11-S07`

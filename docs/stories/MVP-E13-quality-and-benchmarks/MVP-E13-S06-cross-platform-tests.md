# MVP-E13-S06 — Run Cross-platform Contract Tests

## Outcome

One shared suite verifies platform-sensitive CLI behavior on every published
OS/RID entry.

## Design

- [Output and platform](../../design/quality.md#output-and-platform)

## Boundary

The suite complements package matrix smoke tests with behavioral assertions;
it does not add untested platforms to the support claim.

## Acceptance

- Paths, locations, LF-only TOON, executable discovery, hook merge/removal,
  permissions, locale, and process-tree cancellation are covered.
- Failures retain platform and toolchain evidence suitable for the release
  manifest.

## Verification

- The same test selection runs successfully on every declared platform matrix
  job.

## Dependencies

- `MVP-E12-S05`
- `MVP-E13-S02`
- `MVP-E13-S03`
- `MVP-E13-S05`

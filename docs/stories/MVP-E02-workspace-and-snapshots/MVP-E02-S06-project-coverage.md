# MVP-E02-S06 — Report Project and Framework Coverage

## Outcome

Results distinguish supported, unsupported, broken, unrestored, and
multi-targeted project variants.

## Design

- [Multi-targeting](../../design/workspace.md#multi-targeting)
- [Project and language support](../../design/workspace.md#project-and-language-support)
- [Restore and broken projects](../../design/workspace.md#restore-and-broken-projects)

## Boundary

Passive inspection reports missing restore or SDK requirements but never
repairs them implicitly.

## Acceptance

- Default and complete framework selection report exact variant coverage.
- Failed and unsupported projects remain in coverage denominators with an
  actionable reason.

## Verification

- Fixtures cover SDK-style C#, non-C#, unsupported projects, multiple TFMs,
  missing assets, and unavailable SDKs.

## Dependencies

- `MVP-E02-S05`
- `MVP-E01-S03`

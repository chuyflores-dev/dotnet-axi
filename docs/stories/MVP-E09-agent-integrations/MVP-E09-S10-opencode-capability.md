# MVP-E09-S10 — Report OpenCode as Unsupported

## Outcome

`setup opencode` returns a stable not-supported capability result during the
MVP.

## Design

- [Explicit setup](../../design/agent-integration.md#explicit-setup)

## Boundary

No partial OpenCode configuration or files are created.

## Acceptance

- The result identifies the unavailable capability and the planned product
  phase without presenting it as an internal failure.
- Repository and user scope requests produce no writes.

## Verification

- Process tests assert structured output, exit behavior, and an unchanged
  filesystem for both scopes.

## Dependencies

- `MVP-E09-S01`

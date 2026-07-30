# MVP-E12-S02 — Verify Global and Local Invocation

## Outcome

Global-tool and local-manifest installation expose the same CLI behavior.

## Design

- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)

## Boundary

Installation location may change invocation syntax, but never command
semantics or output schema.

## Acceptance

- Direct global invocation and `dotnet tool run` execute the same packaged
  version and representative commands.
- Executable discovery and setup receive the correct invocation for each
  installation type.

## Verification

- Isolated smoke tests install, invoke, update-in-place, uninstall, and compare
  both installation modes.

## Dependencies

- `MVP-E12-S01`
- `MVP-E01-S08`

# MVP-E09-S01 — Define Setup Contracts

## Outcome

Agent setup, repair, and removal use typed plans and results that disclose
scope and every affected target.

## Design

- [Explicit setup](../../design/agent-integration.md#explicit-setup)

## Boundary

No integration changes state until a user invokes an explicit setup or removal
command.

## Acceptance

- Repository/user scope, adapter, action, target path, planned change,
  conflict, backup, and result can be represented.
- Dry planning and applied results use the same deterministic target model.

## Verification

- Contract tests cover install, no-op, repair, removal, conflict, unsupported,
  and policy-disabled outcomes.

## Dependencies

- `MVP-E01-S03`
- `MVP-E11-S01`

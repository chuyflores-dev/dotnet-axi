# MVP-E11-S10 — Enforce Setup Trust Boundaries

## Outcome

Agent setup preserves trust databases, managed policy, unrelated
configuration, and explicit repository/user scope.

## Design

- [Setup](../../design/runtime-and-distribution.md#setup)
- [Codex hooks](../../design/agent-integration.md#codex-hooks)

## Boundary

Setup reports required user action but never bypasses review, weakens policy,
or writes outside the selected scope.

## Acceptance

- Every adapter proves its targets remain inside repository or explicitly
  selected user scope.
- Trust-required, managed-disabled, protected, and unknown configuration states
  fail or plan safely without false success.

## Verification

- Security fixtures cover path escape, symlink substitution, trust databases,
  managed policy, protected files, malicious existing entries, and both scopes.

## Dependencies

- `MVP-E09-S03`
- `MVP-E09-S04`
- `MVP-E09-S05`
- `MVP-E11-S07`

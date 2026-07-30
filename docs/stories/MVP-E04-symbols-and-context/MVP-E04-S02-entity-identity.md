# MVP-E04-S02 — Create Stateless Entity Identity

## Outcome

Declaration results carry opaque versioned IDs that resolve to the same
unchanged entity in a fresh process without retained state.

## Design

- [Stateless entity identity](../../design/search-and-context.md#stateless-entity-identity)

## Boundary

The encoding is internal; callers depend only on deterministic resolution and
versioned error behavior.

## Acceptance

- Identity includes stable declaration meaning plus sufficient content and
  location fingerprinting.
- Cache deletion and process restart do not change resolution for unchanged
  declarations.

## Verification

- Identity tests cover overloads, partial declarations, moves that preserve
  identity, fresh processes, and deleted tool state.

## Dependencies

- `MVP-E04-S01`
- `MVP-E02-S08`

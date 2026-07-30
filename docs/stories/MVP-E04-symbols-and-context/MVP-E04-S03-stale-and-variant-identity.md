# MVP-E04-S03 — Protect Stale and Variant Identity

## Outcome

Changed declarations fail safely as stale, while project and framework
variants remain explicit.

## Design

- [Stateless entity identity](../../design/search-and-context.md#stateless-entity-identity)
- [Multi-targeting](../../design/workspace.md#multi-targeting)

## Boundary

An ID never silently binds to a different overload, declaration, owner, or
compiler variant.

## Acceptance

- Stale resolution returns `evidence.stale_id`, replacement candidates, and a
  concrete query.
- One logical declaration can expose distinct owner/configuration variants
  without collapsing different compiler meaning.

## Verification

- Mutation fixtures change signatures, overloads, locations, projects, and
  target frameworks and assert safe resolution.

## Dependencies

- `MVP-E04-S02`
- `MVP-E02-S06`

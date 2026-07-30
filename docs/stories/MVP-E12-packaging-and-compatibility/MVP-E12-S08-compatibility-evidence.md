# MVP-E12-S08 — Publish Compatibility Evidence

## Outcome

Each release records the exact tool, SDK, MSBuild, Roslyn, Git, `rg`, AST-grep,
C# grammar, OS, and RID versions it tested.

## Design

- [Compatibility baseline](../../design/runtime-and-distribution.md#compatibility-baseline)

## Boundary

The evidence is generated from completed matrix results and does not imply
support for absent combinations.

## Acceptance

- The manifest is deterministic, machine-readable, tied to the release
  artifact, and distinguishes supported, unsupported, and unverified entries.
- Missing required matrix evidence blocks the compatibility claim.

## Verification

- Manifest tests compare package identity with matrix artifacts and reject
  missing, conflicting, or stale evidence.

## Dependencies

- `MVP-E12-S03`
- `MVP-E12-S04`
- `MVP-E12-S05`
- `MVP-E12-S06`
- `MVP-E12-S07`

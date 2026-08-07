# MVP-E11-S12 — Restore Safe Passive Tool Execution

## Outcome

Passive discovery may run only trusted, bounded tool commands that cannot
execute repository code, access the network, or write product state.

## Design

- [Passive and executing operations](../../design/foundations.md#passive-and-executing-operations)
- [Network and telemetry](../../design/runtime-and-distribution.md#network-and-telemetry)
- [Process and secret safety](../../design/runtime-and-distribution.md#process-and-secret-safety)
- [Required and optional dependencies](../../design/runtime-and-distribution.md#required-and-optional-dependencies)

## Boundary

This story does not grant a general child-process capability to passive
commands. Executables controlled by the workspace, untrusted command shapes,
repository-code execution, network access, and repository or persistent
product-state writes remain denied.

## Acceptance

- Production changed-scope search uses the guarded Git inspector, compatible
  literal text search may use the guarded `rg` accelerator, and home/version
  output performs bounded compatibility probes.
- The Git inspector admits only its fixed read-only command shapes and uses
  bounded owned process containment; passive SDK probing cannot load
  workspace-provided SDK assemblies or block on non-regular `global.json`
  inputs.
- PATH resolution ignores relative entries and rejects executable candidates
  lexically or physically controlled by the workspace.
- Missing, incompatible, failing, shadowed, or unsuitable optional tools retain
  the documented typed failure or built-in degradation behavior.

## Verification

- Installed-CLI and integration tests prove the accepted Git, `rg`, and
  capability paths while monitoring for repository-code execution, network
  attempts, source writes, and workspace-controlled executable invocation.

## Dependencies

- `MVP-E02-S04`
- `MVP-E03-S05`
- `MVP-E11-S02`
- `MVP-E11-S04`
- `MVP-E12-S03`
- `MVP-E12-S04`

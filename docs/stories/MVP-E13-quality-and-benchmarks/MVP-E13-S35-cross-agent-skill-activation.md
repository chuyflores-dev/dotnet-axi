# MVP-E13-S35 — Remove Cross-agent Skill Activation Noise

## Outcome

The portable `dotnet-axi` Agent Skill activates without host-specific
reference noise, and the Codex discovery benchmark measures that activation
without deterministic first-read failures or leading-assignment gaps.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

This story does not add product commands, package Agent Skill files in NuGet,
install hooks, publish a release, or dispatch a paid benchmark.

## Acceptance

- The portable distribution contains only one bounded, agent-neutral
  `SKILL.md`; host-specific operation remains with the host or repository.
- The NuGet package remains tool-only.
- Both benchmark conditions receive the same pinned `sed` reader in the sealed
  raw-tool path, and preparation proves it can read the complete skill before
  paid execution.
- Local Codex discovery probes remain bounded while allowing 30 seconds for
  cold-start contention in parallel CI.
- Activation reconciliation maps source-pinned `dnx` routes when valid POSIX
  environment assignments precede the executable without accepting `PATH`
  shadowing, quoted or malformed assignment names, unquoted non-POSIX
  whitespace, or POSIX assignment syntax in PowerShell.
- Corrected evidence pins harness `2.3.0` and Codex adapter `1.7.0` rather than
  relabeling retained evidence.

## Verification

- Generated-skill tests prove the single-file portable distribution and
  tool-only package boundary.
- Preparation tests reject a missing or unusable bounded reader.
- A regression contract fixes the local Codex probe budget at 30 seconds.
- Reconciliation tests cover valid leading assignments, invalid names,
  non-POSIX whitespace, and `PATH` shadowing.
- Canonical restore, Release build, and tests pass.

## Dependencies

- `MVP-E13-S17`
- `MVP-E13-S34`

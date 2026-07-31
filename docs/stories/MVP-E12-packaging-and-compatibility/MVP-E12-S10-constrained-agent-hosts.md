# MVP-E12-S10 - Verify Constrained Agent Hosts

## Outcome

The packaged CLI behaves predictably across representative agent sandbox, network, and worktree restrictions on the supported platform matrix.

## Design

- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)
- [Constrained host failures](../../design/runtime-and-distribution.md#constrained-host-failures)
- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)

## Boundary

The matrix verifies product behavior under enforced restrictions.
It does not claim every agent vendor uses the same sandbox or that `dotnet-axi` can bypass host policy.

## Acceptance

- Declared platforms exercise read-only source, a writable active worktree, an external worktree with and without an approved root, disabled network, and protected or shared Git metadata.
- Passive commands remain network-free and succeed whenever their required reads are allowed; executing commands either succeed within policy or return the observable constrained-host cause without hanging.
- Restriction manifests record OS/RID, filesystem roots, network policy, working directory, invocation form, and expected outcome without secrets.
- Repeated scenarios leave no stale workers, descendants, locks, or repository artifacts.

## Verification

- Deterministic CI fixtures exercise OS-enforced restrictions without paid agent execution.
- A declared pairwise matrix covers every restriction, supported OS family, and package/local/global/`dnx` invocation form at least once without requiring the full Cartesian product.
- Retained results prove expected typed failures and bounded cleanup for every declared case.

## Dependencies

- `MVP-E11-S11`
- `MVP-E12-S02`
- `MVP-E12-S05`
- `MVP-E13-S03`

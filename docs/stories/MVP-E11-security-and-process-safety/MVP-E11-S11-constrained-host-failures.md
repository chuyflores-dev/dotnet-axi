# MVP-E11-S11 - Classify Constrained Host Failures

## Outcome

Agents receive a bounded, typed result when an observable host filesystem, process, or network restriction prevents an otherwise supported operation.

## Design

- [Constrained host failures](../../design/runtime-and-distribution.md#constrained-host-failures)
- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)

## Boundary

The tool reports observed restrictions without claiming to detect or control a specific agent sandbox.
It never broadens permissions, changes checkout, or retries through a less restricted path.
Process-tree termination mechanics remain in `MVP-E11-S04`; this story adds failure mapping and bounded no-loop recovery.

## Acceptance

- Observable process-start, filesystem-permission, host-reported network-policy, timeout, cancellation, output-limit, and dependency failures map to distinct typed causes with bounded diagnostics.
- A dependency network error is not relabeled as a host-policy denial without authoritative host evidence.
- Results preserve dependency exit information and identify a safe blocked path or destination when available.
- A correction requires an explicit caller or user boundary change; unchanged restrictions do not trigger an automatic retry.

## Verification

- Cross-platform fixtures cover denied launch, read-only build output, host-reported network denial, ordinary dependency network failure, cancellation, timeout, and output overflow.
- Contract tests prove the same restriction is not relabeled as an unsupported product or successful operation and that unchanged policy does not trigger a retry.

## Dependencies

- `MVP-E08-S02`
- `MVP-E08-S03`
- `MVP-E11-S03`
- `MVP-E11-S04`

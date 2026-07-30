# MVP-E11 — Security and Process Safety

## Outcome

Passive and executing operations preserve the documented network, telemetry,
process, secret, artifact, and source-write boundaries.

## Scope

- Passive/executing classification and side-effect disclosure.
- Argument-list process invocation, controlled environment, bounded capture,
  cancellation, timeout, and tree termination.
- Secret redaction and untrusted-output encoding.
- Diagnostic artifact isolation, permissions, retention, cleanup, and
  symlink/reparse-point defenses.
- Setup and repository-code execution safety controls.

## Boundary

The product discloses operating-system permission limits and does not claim to
be a sandbox unless one is actually enforced.

## Design

- [Runtime and distribution](../../design/runtime-and-distribution.md)
- [Design foundations](../../design/foundations.md)
- [Analysis and execution](../../design/analysis-and-execution.md)
- [Agent integration](../../design/agent-integration.md)

## Dependencies

- `MVP-E01`
- `MVP-E08`
- `MVP-E09`

## Stories

- [MVP-E11-S01 — Classify operation effects](MVP-E11-S01-effect-classification.md)
- [MVP-E11-S02 — Enforce passive operation boundaries](MVP-E11-S02-passive-boundary.md)
- [MVP-E11-S03 — Disclose network and execution effects](MVP-E11-S03-effect-disclosure.md)
- [MVP-E11-S04 — Harden child-process execution](MVP-E11-S04-child-process-safety.md)
- [MVP-E11-S05 — Protect structured output and secrets](MVP-E11-S05-output-and-secret-safety.md)
- [MVP-E11-S06 — Create isolated diagnostic artifacts](MVP-E11-S06-diagnostic-artifacts.md)
- [MVP-E11-S07 — Defend filesystem writes](MVP-E11-S07-filesystem-write-safety.md)
- [MVP-E11-S08 — Authorize source writes](MVP-E11-S08-source-write-authorization.md)
- [MVP-E11-S09 — Clean retained artifacts](MVP-E11-S09-artifact-cleanup.md)
- [MVP-E11-S10 — Enforce setup trust boundaries](MVP-E11-S10-setup-trust.md)

## Complete when

- Passive operations cause no tool-initiated network, restore, telemetry, or
  repository-code execution.
- Adversarial process, path, output, secret, and artifact scenarios fail
  safely without corrupting structured output.

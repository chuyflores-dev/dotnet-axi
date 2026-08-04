# MVP-E13 — Quality Gates and Benchmarks

## Outcome

The release has repeatable evidence for correctness, compatibility, security,
performance, and complete agent-task outcomes.

## Scope

- Shared unit, integration, oracle, golden-output, cross-platform, and security
  test infrastructure.
- Deterministic large-repository fixture and cold P95 benchmark harness.
- Agent-neutral task and runner contracts with Codex-first and later Claude
  adapters.
- Release evidence for the documented performance and agent-experience gates.

## Boundary

Capability stories own their focused tests. This epic owns shared harnesses,
cross-cutting fixtures, and release-level evidence rather than delaying all
testing until the end.

## Design

- [Quality](../../design/quality.md)
- [CLI and output contract](../../design/output-contract.md)
- [Runtime and distribution](../../design/runtime-and-distribution.md)

## Dependencies

- `MVP-E01` through `MVP-E12`, according to the capability under test.

## Stories

- [MVP-E13-S01 — Build the integration fixture factory](MVP-E13-S01-fixture-factory.md)
- [MVP-E13-S02 — Add workspace and project fixtures](MVP-E13-S02-workspace-fixtures.md)
- [MVP-E13-S03 — Add worktree and failure fixtures](MVP-E13-S03-edge-fixtures.md)
- [MVP-E13-S04 — Build structural and Roslyn oracles](MVP-E13-S04-semantic-oracles.md)
- [MVP-E13-S05 — Verify TOON conformance](MVP-E13-S05-toon-conformance.md)
- [MVP-E13-S06 — Run cross-platform contract tests](MVP-E13-S06-cross-platform-tests.md)
- [MVP-E13-S07 — Run security adversarial tests](MVP-E13-S07-security-tests.md)
- [MVP-E13-S08 — Generate the large-repository fixture](MVP-E13-S08-large-repository-fixture.md)
- [MVP-E13-S09 — Measure cold P95 performance](MVP-E13-S09-performance-harness.md)
- [MVP-E13-S10 — Define the agent-task corpus](MVP-E13-S10-agent-task-corpus.md)
- [MVP-E13-S11 — Build the agent benchmark runner](MVP-E13-S11-agent-benchmark-runner.md)
- [MVP-E13-S12 — Produce release-gate evidence](MVP-E13-S12-release-gates.md)
- [MVP-E13-S13 — Add the Codex benchmark adapter](MVP-E13-S13-codex-benchmark-adapter.md)
- [MVP-E13-S14 — Add the Claude benchmark adapter](MVP-E13-S14-claude-benchmark-adapter.md)
- [MVP-E13-S15 — Measure 0.3.0 Codex discovery tasks](MVP-E13-S15-measure-0.3.0-codex-discovery.md)
- [MVP-E13-S16 — Add 0.4.0 symbol-context tasks](MVP-E13-S16-symbol-context-corpus.md)
- [MVP-E13-S17 — Measure the 0.4.0 Codex subset](MVP-E13-S17-measure-0.4.0-codex-subset.md)

## Complete when

- Required correctness, platform, security, and cold-performance gates pass on
  the published matrix and designated runner.
- Codex and Claude agent-task runs independently demonstrate the MVP
  release-bar outcome against the documented raw-tool baseline with
  reproducible evidence.

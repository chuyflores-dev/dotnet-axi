# MVP-E13 — Quality Gates and Benchmarks

## Outcome

The release has repeatable evidence for correctness, compatibility, security,
performance, and complete agent-task outcomes.

S40 supersedes the compiled runner and provider-adapter work items below. Their
story files remain historical records; they do not define the current release
gate.

## Scope

- Shared unit, integration, oracle, golden-output, cross-platform, and security
  test infrastructure.
- Deterministic large-repository fixture and cold P95 benchmark harness.
- Deterministic CLI checks, a manual one-task benchmark script, and occasional
  paired runs for named agent-experience claims.
- Release evidence for the documented correctness, performance, and canary
  gates.

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
- [MVP-E13-S16 — Add 0.5.0 symbol-context tasks](MVP-E13-S16-symbol-context-corpus.md)
- [MVP-E13-S17 — Prove dnx-first 0.4.0 self-hosting](MVP-E13-S17-prove-dnx-first-0.4.0-self-hosting.md)
- [MVP-E13-S18 — Correct Codex benchmark result reconciliation](MVP-E13-S18-codex-benchmark-reconciliation.md)
- [MVP-E13-S19 — Add 0.6.0 semantic-relationship and graph tasks](MVP-E13-S19-semantic-relationship-corpus.md)
- [MVP-E13-S20 — Measure the 0.6.0 Codex subset](MVP-E13-S20-measure-0.6.0-codex-subset.md)
- [MVP-E13-S21 — Add 0.7.0 analysis and SDK-execution tasks](MVP-E13-S21-analysis-and-sdk-corpus.md)
- [MVP-E13-S22 — Measure the 0.7.0 Codex subset](MVP-E13-S22-measure-0.7.0-codex-subset.md)
- [MVP-E13-S23 — Add 0.8.0 configuration and validation tasks](MVP-E13-S23-configuration-and-validation-corpus.md)
- [MVP-E13-S24 — Measure the 0.8.0 Codex subset](MVP-E13-S24-measure-0.8.0-codex-subset.md)
- [MVP-E13-S25 — Add 0.9.0 safe agent-integration tasks](MVP-E13-S25-safe-agent-integration-corpus.md)
- [MVP-E13-S26 — Measure the 0.9.0 Codex subset](MVP-E13-S26-measure-0.9.0-codex-subset.md)
- [MVP-E13-S27 — Measure the 0.9.0 Claude subset](MVP-E13-S27-measure-0.9.0-claude-subset.md)
- [MVP-E13-S28 — Measure the 0.10.0 Codex release gate](MVP-E13-S28-measure-0.10.0-codex-release-gate.md)
- [MVP-E13-S29 — Measure the 0.10.0 Claude release gate](MVP-E13-S29-measure-0.10.0-claude-release-gate.md)
- [MVP-E13-S30 — Measure the 1.0.0 Codex release gate](MVP-E13-S30-measure-1.0.0-codex-release-gate.md)
- [MVP-E13-S31 — Measure the 1.0.0 Claude release gate](MVP-E13-S31-measure-1.0.0-claude-release-gate.md)
- [MVP-E13-S32 — Certify the 1.0.0 release bar](MVP-E13-S32-certify-1.0.0-release-bar.md)
- [MVP-E13-S33 — Measure the 0.5.0 Codex symbol-context subset](MVP-E13-S33-measure-0.5.0-codex-symbol-context.md)
- [MVP-E13-S34 — Separate Agent Skill installation from NuGet packaging](MVP-E13-S34-separate-skill-distribution.md)
- [MVP-E13-S35 — Remove cross-agent skill activation noise](MVP-E13-S35-cross-agent-skill-activation.md)
- [MVP-E13-S36 — Repair the 0.5.0 Codex symbol-context release gate](MVP-E13-S36-repair-0.5.0-codex-release-gate.md)
- [MVP-E13-S40 — Simplify the agent outcome benchmark](MVP-E13-S40-simplify-agent-outcome-benchmark.md)
- [MVP-E13-S41 — Add shipped semantic-relationship benchmark task](MVP-E13-S41-add-shipped-semantic-relationship-benchmark-task.md)

## Complete when

- Required correctness, platform, security, and cold-performance gates pass on
  the published matrix and designated runner.
- The release candidate passes the applicable candidate agent canary.
- Any named comparative agent-experience claim has separate paired raw-tool
  evidence for the exact agent, model, corpus, and harness.

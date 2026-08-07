# MVP-E09-S14 — Improve Codex Activation of the Source-discovery Agent Skill

## Outcome

Applicable Codex source-discovery tasks reliably expose and favor the shipped
`dotnet-axi` Agent Skill when the installed CLI reports the required
capability.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Evidence

The retained 0.3.0 candidate condition made `dnaxi` available and loaded the
packaged skill, but Codex invoked `dnaxi` in 0 of 35 runs and continued using
raw discovery tools.

## Boundary

This story improves source-discovery skill activation. It does not add symbol
context guidance from `MVP-E09-S13` or reinterpret the retained 0.3.0 evidence
from `MVP-E13-S15`.

## Acceptance

- Skill metadata and generated guidance clearly route applicable .NET file,
  literal, regular-expression, and stable-syntax discovery through an
  available capable `dnaxi`.
- Exact version-pinned command guidance does not require a redundant help
  invocation before every known route.
- Direct reads of already-known files and capability fallback remain explicit
  escape hatches.
- Activation is achieved without condition-specific benchmark prompts, hidden
  hooks, ambient configuration, broader sandbox permissions, or network
  access.
- Committed, packaged, structured-help, and home-view guidance remain generated
  from one source and byte-consistent where required.

## Verification

- Golden generation and packaged-skill tests cover applicable discovery
  routing, direct-read and capability fallbacks, and byte consistency.
- Benchmark evidence distinguishes skill availability from actual `dnaxi`
  activation and reports non-activation honestly.

## Dependencies

- `MVP-E09-S12`

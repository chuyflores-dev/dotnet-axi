# MVP-E09-S14 — Activate Dnx-first Source Discovery in Codex

## Outcome

Applicable Codex source-discovery tasks reliably load the shipped Agent Skill
and select the exact version-pinned `dnx` invocation when the candidate
reports the required capability.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Evidence

The retained 0.3.0 candidate condition made `dnaxi` available and loaded the
packaged skill, but Codex invoked `dnaxi` in 0 of 35 runs and continued using
raw discovery tools.

## Boundary

This story improves source-discovery skill activation and the compact command
guidance that supports it. It does not add symbol context guidance from
`MVP-E09-S13`, install hooks or a persistent tool, or reinterpret the retained
0.3.0 evidence from `MVP-E13-S15`.

## Acceptance

- Trigger-shaped skill metadata and generated guidance route applicable .NET
  file, literal, regular-expression, and stable-syntax discovery through
  `dnx dnaxi@<exact-version> --verbosity quiet --`.
- Exact version-pinned guidance does not require a redundant help invocation
  before every known route.
- No-argument and help output retain compact workspace or command content and
  a few contextual next steps rather than embedding the full Agent Skill.
- Suggestions and truncation recovery commands remain directly runnable
  through the selected `dnx` command vector and never fall back to bare
  `dnaxi` when no persistent command was selected.
- Direct reads of already-known files and capability fallback remain explicit
  escape hatches.
- Activation is achieved without condition-specific benchmark prompts, hidden
  hooks, ambient configuration, broader sandbox permissions, or tool-initiated
  network access after the candidate package is available locally.
- Static guidance fragments remain generated from one source; the committed
  and packaged Agent Skill stay byte-identical without requiring the full
  skill document to appear in every CLI response.

## Verification

- Golden generation, packaged-skill, structured-help, home-view, and recovery
  tests cover dnx routing, compact output, direct-read and capability
  fallbacks, and required byte consistency.
- A local-feed candidate smoke run proves the generated commands execute
  through `dnx` without a global or local tool installation.
- Benchmark evidence distinguishes skill availability from actual `dnx`
  activation and reports non-activation honestly.

## Dependencies

- `MVP-E09-S12`
- `MVP-E12-S34`

# MVP-E13-S34 — Separate Agent Skill Installation from NuGet Packaging

## Outcome

The `dnaxi` package carries only the .NET tool, while the repository Agent
Skill is installed independently and is model-visible through the host's
supported skill-discovery path before any measured 0.4.0 benchmark runs.

## Design

- [Agent integration](../../design/agent-integration.md#generated-agent-skill)
- [Runtime and distribution](../../design/runtime-and-distribution.md#platform-and-packaging)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

This story corrects artifact ownership and deterministic benchmark exposure.
It does not publish packages, install `dnaxi` persistently, dispatch a paid
agent series, or change discovery commands and task oracles.

## Acceptance

- The `dnaxi` NuGet package and symbols contain no Agent Skill files; package
  verification treats the archive as the tool distribution only.
- The checked-in `skills/dotnet-axi` directory is the installable Agent Skill
  source and carries the exact 0.4.0 `dnx dnaxi@0.4.0` guidance required by the
  release candidate.
- Benchmark preparation pins the repository skill independently from the
  candidate package and exposes it only to the candidate through Codex's
  supported `.agents/skills/dotnet-axi` discovery path.
- A network-free prompt-input preflight proves the candidate skill is visible,
  the baseline does not expose it, and the model-visible guidance contains the
  exact source-pinned 0.4.0 invocation before paid execution is allowed.
- The pinned local NuGet feed remains responsible only for resolving the exact
  candidate tool through `dnx`; neither condition persistently installs
  `dnaxi`.

## Verification

- Package inspection rejects any `skills/` archive entry.
- Deterministic preparation and adapter tests cover separated skill and tool
  hashes, condition-equivalent workspaces, discovery-root exposure, and
  preflight failure on missing, leaked, or incorrectly versioned guidance.
- Canonical restore, release build, and test commands pass.

## Dependencies

- `MVP-E13-S17`
- `MVP-E09-S14`
- `MVP-E12-S34`

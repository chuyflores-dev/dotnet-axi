# MVP-E09 — Agent Integrations

## Outcome

Claude Code and Codex can be explicitly configured to discover and use the
same deterministic CLI contracts safely.

## Scope

- Repository and user setup for Claude Code and Codex.
- Bounded passive session-start context.
- Structural configuration merge, repair, backup, and removal.
- A portable Agent Skill generated from the same guidance as the CLI.
- Trust, policy, executable-path, and unsupported-format reporting.

## Boundary

OpenCode setup is an explicit not-supported capability in the MVP. Integrations
do not read or retain agent transcripts.

## Design

- [Agent integration](../../design/agent-integration.md)
- [CLI and output contract](../../design/output-contract.md)
- [Runtime and distribution](../../design/runtime-and-distribution.md)

## Dependencies

- `MVP-E01`
- `MVP-E02`
- `MVP-E07`

## Stories

- [MVP-E09-S01 — Define setup contracts](MVP-E09-S01-setup-contracts.md)
- [MVP-E09-S02 — Resolve a stable invocation](MVP-E09-S02-invocation-resolution.md)
- [MVP-E09-S03 — Edit agent configuration safely](MVP-E09-S03-agent-configuration-editor.md)
- [MVP-E09-S04 — Set up Claude Code](MVP-E09-S04-claude-code-setup.md)
- [MVP-E09-S05 — Set up Codex](MVP-E09-S05-codex-setup.md)
- [MVP-E09-S06 — Emit session-start context](MVP-E09-S06-session-start-context.md)
- [MVP-E09-S07 — Ship the Agent Skill](MVP-E09-S07-agent-skill.md)
- [MVP-E09-S08 — Repair integrations](MVP-E09-S08-repair.md)
- [MVP-E09-S09 — Remove integrations](MVP-E09-S09-removal.md)
- [MVP-E09-S10 — Report OpenCode as unsupported](MVP-E09-S10-opencode-capability.md)
- [MVP-E09-S11 — Guide safe Codex worker startup](MVP-E09-S11-codex-worker-startup.md)
- [MVP-E09-S12 — Teach source discovery in the Agent Skill](MVP-E09-S12-source-discovery-skill.md)
- [MVP-E09-S13 — Teach symbol context in the Agent Skill](MVP-E09-S13-symbol-context-skill.md)

## Complete when

- Install, repair, and removal are idempotent and preserve unrelated agent
  configuration.
- The portable skill can invoke the packaged tool on demand without requiring
  agent-specific configuration.
- Generated guidance directs agents to applicable validation evidence before
  claiming completion.

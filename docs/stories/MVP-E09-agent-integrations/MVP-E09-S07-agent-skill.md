# MVP-E09-S07 — Ship the Agent Skill

## Outcome

Agents can install a portable `dotnet-axi` skill and invoke the packaged tool
on demand without a permanent global installation.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)

## Boundary

This story ships guidance and its distribution artifact.
It does not install hooks, edit agent configuration, include live workspace state, or depend on one agent's hidden prompting behavior.
Portable discovery by another harness does not make that harness a supported setup adapter.
Guidance never changes the host sandbox, approval policy, trust state, or network policy.

## Acceptance

- `skills/dotnet-axi/SKILL.md` uses portable Agent Skills metadata and is
  discoverable for repository or user installation.
- Guidance defines when to use or skip `dnaxi`, teaches one-shot `dnx`
  invocation, and treats the invoked version's help and capabilities as
  authoritative.
- Guidance exposes only shipped capabilities and teaches their documented
  evidence, safety, and completion flow.
- The portable skill keeps agent-neutral guidance concise and generates a progressive-disclosure Codex reference covering writable worktree roots, protected Git metadata, explicit network operations, scoped approvals, noninteractive sandbox modes, and bounded no-loop recovery.
- Skill, structured-help, and home-view guidance share one canonical source;
  the committed and packaged skill copies are byte-identical.
- A generation check detects stale derived content.

## Verification

- Golden generation tests cover deterministic output, stale detection, bounded guidance, use/skip routing, host-restriction handling, and required completion language.
- Isolated compatibility tests discover and install the skill at repository
  and user scope, inspect the package copy, and exercise representative `dnx`
  guidance against the packaged CLI.

## Dependencies

- `MVP-E01-S07`
- `MVP-E01-S08`
- `MVP-E12-S01`
- `MVP-E12-S02`

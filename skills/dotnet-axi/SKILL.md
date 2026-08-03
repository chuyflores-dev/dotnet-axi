---
name: dotnet-axi
description: Use dotnet-axi to obtain deterministic structured evidence for .NET workspaces when the invoked version reports the needed capability, including workspace or source discovery, semantic evidence, impact, analysis, and validation. Use for .NET repository investigation and completion checks; skip for non-.NET work and direct reads of already-known files.
---

# Use dotnet-axi

- Use this skill as an on-demand guide. Do not install hooks, edit agent configuration, include live workspace state, or change the host sandbox, approvals, trust, or network policy.
- Treat portable discovery by a harness as skill availability, not evidence that the harness is a supported setup adapter.

## Route the task

- Use dotnet-axi for a .NET workspace when deterministic structured evidence is useful.
- Use only a workspace, source, semantic, impact, analysis, or validation capability reported by the invoked version.
- Skip dotnet-axi for non-.NET work.
- Skip dotnet-axi when a direct read of an already-known file is the smaller operation.
- Skip any capability that the invoked version does not report and use an available direct tool instead.

## Invoke on demand

1. Prefer a verified local or global `dnaxi <command>` invocation only when one is already available.
2. Otherwise run one shot with `dnx dotnet-axi -- <command>`. Do not require a permanent global installation.
3. Start with `dnx dotnet-axi --` for the passive home view or `dnx dotnet-axi -- --help` for structured help. Use `dnx dotnet-axi -- --version` when version identity matters.
4. Treat the invoked version's structured help, version, and reported capabilities as authoritative. Never use a command or option that it does not expose.
5. Remember that `dnx` package resolution may download or restore the tool. Keep that network operation explicit and subject to host policy.

## Follow reported capabilities

Apply this flow only when the invoked version reports the relevant capability.

- Use text search for literals.
- Use structural search for syntax shape.
- Use Roslyn operations for exact identity.
- Inspect impact before public changes.
- Request bounded context.
- Run fast validation during work.
- Run standard validation before completion.

## Preserve evidence and safety

- Start with passive discovery and inspect command classification before allowing repository-code execution, network access, or writes.
- Treat dnx package resolution as an explicit operation that may require network access; never bypass the host policy.
- Keep evidence scoped and distinguish verified facts from candidates, uncertainty, and incomplete coverage.
- Treat denied filesystem, process, or network access as a host restriction; retry only after a confirmed policy change and do not loop.

## Complete with evidence

Do not claim completion solely because files changed. When the invoked version exposes validate, use the strongest applicable `dnaxi validate` evidence available within the requested scope. Otherwise run the strongest applicable project validation and report the evidence and any gaps.

Report the command, requested scope, result status, resolution, coverage, confidence when applicable, and any remaining blocker or validation gap.

## Load host-specific guidance only when needed

When running under Codex, read [Codex sandbox operation](references/codex.md) before requesting access, operating in a worktree, or starting a noninteractive worker. Other agents must follow their own host controls and must not treat Codex flags as portable requirements.

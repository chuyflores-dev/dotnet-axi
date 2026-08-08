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

1. Prefer an already-verified persistent invocation only when one was selected: global `dnaxi <command>` or local `dotnet tool run dnaxi -- <command>`.
2. Otherwise run one shot with `dnx dnaxi@<exact-version> --verbosity quiet -- <command>`. Keep the exact version pin and do not require a permanent installation.
3. Start with `dnx dnaxi@<exact-version> --verbosity quiet --` for the passive home view or `dnx dnaxi@<exact-version> --verbosity quiet -- --help` for structured help. Use `dnx dnaxi@<exact-version> --verbosity quiet -- --version` when version identity matters.
4. Treat the invoked version's structured help, version, and reported capabilities as authoritative. Never use a command or option that it does not expose.
5. Remember that `dnx` package resolution may download or restore the tool. Keep that network operation explicit and subject to host policy.

## Follow reported capabilities

Apply this flow only when the invoked version reports the relevant capability.

- Use text search for literals.
- Use stable syntax queries for syntax shape.
- Use Roslyn operations for exact identity.
- Inspect impact before public changes.
- Request bounded context.
- Run fast validation during work.
- Run standard validation before completion.

## Discover source with bounded queries

1. Before source discovery, inspect the invoked version's structured help for the selected route and its options. If that route is unavailable, use an available direct tool and report the capability gap instead of inventing a command.
2. Find a file by normalized path with `dnx dnaxi@<exact-version> --verbosity quiet -- search file '<path-fragment>' --path <scope> --limit 20`. If the exact file is already known and a direct read is smaller, read it directly.
3. Find literal text with `dnx dnaxi@<exact-version> --verbosity quiet -- search text '<literal>' --path <scope> --limit 20`.
4. Find a .NET regular expression with `dnx dnaxi@<exact-version> --verbosity quiet -- search text '<dotnet-regex>' --regex --path <scope> --limit 20`; narrow the expression or path when a file times out.
5. Find a C# syntax shape by checking `dnx dnaxi@<exact-version> --verbosity quiet -- search syntax --help` and selecting an exposed stable query. For example, use `dnx dnaxi@<exact-version> --verbosity quiet -- search syntax invocation --name SaveChangesAsync --path <scope> --limit 20`.
6. Treat stable syntax results as syntax candidates, never as compiler-verified symbol or type identity.
7. Text search may use compatible `rg` acceleration. When that optional engine is absent, incompatible, or unsuitable for the query, `search text` degrades to its built-in engine with the same stable command behavior.
8. Keep discovery bounded with a narrow `--path` and `--limit`. If output is truncated, follow its `retrieval_command` only when the remaining rows are needed; otherwise use the returned path or match to issue the next narrower file, text, or syntax query instead of dumping broad source.

## Preserve evidence and safety

- Start with passive discovery and inspect command classification before allowing repository-code execution, network access, or writes.
- Treat dnx package resolution as an explicit operation that may require network access; never bypass the host policy.
- Keep evidence scoped and distinguish verified facts from candidates, uncertainty, and incomplete coverage.
- Treat denied filesystem, process, or network access as a host restriction; retry only after a confirmed policy change and do not loop.

## Complete with evidence

Do not claim completion solely because files changed. When the invoked version exposes validate, use the strongest applicable `dnx dnaxi@<exact-version> --verbosity quiet -- validate` evidence available within the requested scope. Otherwise run the strongest applicable project validation and report the evidence and any gaps.

Report the command, requested scope, result status, resolution, coverage, confidence when applicable, and any remaining blocker or validation gap.

## Load host-specific guidance only when needed

When running under Codex, read [Codex sandbox operation](references/codex.md) before requesting access, operating in a worktree, or starting a noninteractive worker. Other agents must follow their own host controls and must not treat Codex flags as portable requirements.

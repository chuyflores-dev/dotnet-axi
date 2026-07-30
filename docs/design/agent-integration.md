# Agent Integration Design

`dotnet-axi` exposes the same deterministic command and evidence contracts to
Claude Code, Codex, OpenCode, developers, and CI. Agent-specific setup MAY
differ; command semantics MUST NOT depend on hidden prompts or one model's
behavior.

## Explicit setup

Integrations are installed only by user-invoked commands:

```bash
dnaxi setup claude-code
dnaxi setup codex
dnaxi setup opencode
```

Claude Code and Codex setup ship in the MVP. Until Phase 2,
`setup opencode` returns a structured not-supported capability rather than
installing a partial integration.

Setup accepts `--scope repository|user`, defaults to `repository`, and supports
`--remove`. It reports every file or plugin entry it would or did change.
Install, repair, and removal are idempotent and preserve unrelated
configuration.

Repository scope writes only inside the selected repository. User scope writes
only after the user explicitly selects it.

## Session-start context

A session-start integration SHOULD emit a compact, directory-scoped passive
home view. Ambient context:

- Describes only the current workspace.
- Is at most 1,000 characters.
- Is smaller than an ordinary explicit query result.
- Excludes deep source content.

Hooks consume only the working directory, event name/source, and minimum
process context needed to locate `dotnet-axi`. They ignore session IDs,
transcript paths, prompt text, and unrelated hook payload.

The tool MUST NOT read, copy, parse, or retain full agent transcripts. A future
opt-in feature MAY retain only tool-owned metadata such as commands run,
tool-modified files, and validation outcomes.

## Path and configuration safety

Setup prefers the PATH-resolved executable when correct, falls back to an
absolute path, repairs outdated paths, and remains idempotent.

Supported hook/plugin configuration is parsed and structurally merged, written
atomically, and backed up recoverably when an existing file is replaced.
Ambiguous, invalid, or protected configuration produces a plan or structured
conflict and is never overwritten wholesale.

Every adapter declares and tests supported agent configuration formats. An
unknown future format fails safely with capability information instead of
guessing a destructive merge.

## Codex hooks

Repository setup uses `<repo>/.codex/hooks.json` or the equivalent repository
`config.toml` hook layer and prefers one representation per layer. It installs
a `SessionStart` command that works from repository subdirectories and emits
only the bounded passive home view.

Setup accounts for these Codex behaviors:

- Hooks are enabled by default.
- Repository hooks require trust review before they run.
- User or managed policy may disable non-managed hooks.
- Multiple hook sources are additive.

Setup reports required trust review and disabled-by-policy states. It MUST NOT
bypass trust, enable a managed-disabled feature, or claim success when the hook
cannot run.

## Generated Agent Skill

The repository ships a portable Agent Skill at
`skills/dotnet-axi/SKILL.md`. Agent Skills tooling can install it for one
repository or for the user:

```bash
npx skills add chuyflores-dev/dotnet-axi --skill dotnet-axi
npx skills add chuyflores-dev/dotnet-axi --skill dotnet-axi -g
```

The skill is an automatically discovered guide rather than a user-facing
slash command. It uses portable `name` and `description` metadata and does not
require one agent's private prompt or configuration format.
Format portability does not expand the supported setup matrix: the MVP
verifies Codex and Claude Code, while `setup opencode` remains explicitly
unsupported.

The skill teaches one-shot `dnx dotnet-axi -- <command>` invocation so an agent
does not require a permanent global-tool installation. A verified local or
global invocation MAY be used when available. Guidance treats the invoked
tool's help, version, and capability output as authoritative and never assumes
that a command exists merely because a newer skill mentions it.

Skill, structured-help, and home-view guidance are generated from one
canonical command-guidance source, with a CI check that detects stale generated
content. The committed skill and the copy carried by the release package are
byte-identical. Generated skills do not contain live workspace state.

Guidance SHOULD say when `dnaxi` is useful and when a direct operation is
smaller. Agents use it for supported .NET workspace discovery, source
discovery, semantic evidence, impact, analysis, and validation. They skip it
for non-.NET work or a simple read of an already-known file.

When the invoked version exposes the relevant capability, guidance SHOULD
teach agents to:

- Use text search for literals.
- Use structural search for syntax shape.
- Use Roslyn operations for exact identity.
- Inspect impact before public changes.
- Request bounded context.
- Run fast validation during work.
- Run standard validation before completion.

Guidance MUST tell agents not to claim completion solely because files changed.
Completion SHOULD use the strongest applicable `dnaxi validate` evidence
available within the requested scope.

## Agent-facing composition

Results include the minimum evidence needed for the next decision: source
location, entity identity, resolution, scope coverage, and applicable
confidence or uncertainty. They MUST NOT make authoritative natural-language
claims beyond Roslyn, MSBuild, AST-grep, Git, analyzer, test, or SDK evidence.

Cheap and broadly required summaries SHOULD be precomputed, including totals,
verified/rejected counts, affected projects, test/validation status,
truncation size, and remaining coverage.

Bounded composite commands SHOULD cover predictable tasks where composition
reduces errors or round trips:

- Structural discovery plus Roslyn verification.
- Symbol context plus selected callers, callees, and tests.
- Changed-file analysis plus affected-project validation.
- Mutation planning plus completeness checks.

Compositions preserve transparent engine, scope, coverage, and confidence
metadata.

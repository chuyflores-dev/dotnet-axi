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

The skill uses trigger-shaped metadata for .NET file, literal,
regular-expression, and stable-syntax discovery and teaches exact
version-pinned `dnx dnaxi@<version> --verbosity quiet -- <command>` invocation
so an agent does not require a permanent global-tool installation. Known
reported routes are invoked directly without a redundant help probe. Help is
used when a route or its options are unknown. A verified local or global
invocation MAY be used only when explicitly selected. Guidance treats the
invoked tool's help, version, and capability output as authoritative and never
assumes that a command exists merely because a newer skill mentions it.

Skill, structured-help, and home-view guidance are generated from one
canonical command-guidance source, with a CI check that detects stale generated
content. The committed skill carries the complete workflow and is the source
installed by Agent Skills tooling; it is distributed independently from the
NuGet tool package. Structured help and the home view expose only the exact
invocation, authority, and three activation steps alongside their command or
workspace content instead of embedding the full skill. Generated skills do not
contain live workspace state.

Guidance SHOULD say when `dnaxi` is useful and when a direct operation is
smaller. Agents use it for supported .NET workspace discovery, source
discovery, semantic evidence, impact, analysis, and validation. They skip it
for non-.NET work or a simple read of an already-known file.

When the invoked version exposes the relevant capability, guidance SHOULD
teach agents to:

- Route path-fragment discovery through bounded file search, while preserving
  a direct read when the exact file is already known and that is smaller.
- Distinguish literal text from .NET regular-expression search and keep both
  path-scoped and bounded.
- Treat compatible `rg` use as optional acceleration: unavailable,
  incompatible, or unsuitable acceleration degrades to the built-in text
  engine without changing the stable command contract.
- Select only stable syntax query kinds exposed by the invoked help, and label
  their results as syntax candidates rather than compiler-verified identity.
- Use a returned path or match to choose the next narrower evidence-producing
  file, text, or syntax query instead of broadly dumping source. A truncated
  result's retrieval command is used only when the omitted rows are needed.
- Use Roslyn operations for exact identity.
- Inspect impact before public changes.
- Request bounded context.
- Run fast validation during work.
- Run standard validation before completion.

Guidance MUST tell agents not to claim completion solely because files changed.
Completion SHOULD use the strongest applicable `dnaxi validate` evidence
available within the requested scope.

## Sandboxed agent operation

The calling agent's sandbox and approval policy are external authorities.
`dotnet-axi`, its setup adapters, and generated guidance MUST NOT widen, bypass, or silently rewrite them.
`dnaxi` and every process it starts inherit the caller's effective filesystem, process, and network boundaries.

The portable skill keeps agent-neutral command guidance in `SKILL.md` and uses progressive-disclosure references for host-specific operation.
A generated `references/codex.md` carries Codex-specific guidance and official-source links; another agent does not receive Codex flags as portable requirements.
Repository `AGENTS.md` remains the place for durable repository conventions, not user-specific permission profiles or a duplicate tool workflow.
This follows Codex's documented separation between [skills](https://learn.chatgpt.com/docs/build-skills), [repository instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md), and the host's [sandbox and approval controls](https://learn.chatgpt.com/docs/sandboxing).

Codex guidance follows these rules:

- Treat the sandbox as the technical boundary and approvals as the mechanism for crossing it.
  Changing the reviewer does not expand access.
  Request only the narrow scope needed for the operation and never recommend full access as an automatic recovery.
  See [Agent approvals and security](https://learn.chatgpt.com/docs/agent-approvals-security).
- Run from the selected repository or worktree as an active workspace root.
  If an external worktree is not writable, add or approve that exact root instead of redirecting build outputs into another checkout.
  Git permits one mutable branch per worktree, as described in [Codex worktrees](https://learn.chatgpt.com/docs/environments/git-worktrees).
- Prefer passive, network-free discovery first.
  Restore, package download, and all other networked operations remain explicit and require the host policy to allow their destinations.
- Treat protected Git or agent configuration metadata, read-only source, denied network, and denied process launch as host restrictions rather than evidence that the product is unsupported.
  Retry only after a confirmed permission or policy change; otherwise return an actionable blocker.
- For automated Codex workers, choose the sandbox explicitly: use `workspace-write` only for implementation and `read-only` for review.
  Prefer ephemeral JSONL execution, capture the final response separately, and bound event-stream silence and total runtime.
  Codex documents these controls in [non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode).
- The benchmark adapter additionally ignores ambient user configuration and
  ambient user and project execution-policy rules, then supplies its exact
  model, reasoning, approval, disabled-network, workspace, and condition
  exposure through argument-list entries. It does not use
  deprecated full-auto behavior or any approval/sandbox bypass. Ignoring user
  configuration does not replace authentication: an isolated `CODEX_HOME` or
  supported API credential is forwarded only when manual dispatch explicitly
  supplies it to the owned Codex process.
- Prefer Codex [subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents) for clean-context delegation inside an active Codex turn because they inherit the parent turn's sandbox and live approval overrides.
  Treat standalone `codex exec` as an owning automation boundary, not a sandbox escape: start it only where the host already permits Codex initialization and access to the selected worktree.
  Do not probe a restricted sandbox with a nested launch and then repeat the same worker outside it.
- A denial before the first `thread.started` event means that no Codex thread identity was observed; it does not prove that the launcher process exited.
  Preserve the original diagnostic, stop polling a stream that never began, and observe the exact launcher process.
  Before any replacement, confirm exit or terminate, wait for, and reap that child; event absence or silence never authorizes a duplicate, and retry still requires a confirmed host-boundary change.

## Agent-facing composition

Results include the minimum evidence needed for the next decision: source
location, entity identity, resolution, scope coverage, and applicable
confidence or uncertainty. They MUST NOT make authoritative natural-language
claims beyond Roslyn, MSBuild, Git, analyzer, test, or SDK evidence.

Cheap and broadly required summaries SHOULD be precomputed, including totals,
verified/rejected counts, affected projects, test/validation status,
truncation size, and remaining coverage.

Bounded composite commands SHOULD cover predictable tasks where composition
reduces errors or round trips:

- Syntax discovery plus Roslyn verification.
- Symbol context plus selected callers, callees, and tests.
- Changed-file analysis plus affected-project validation.
- Mutation planning plus completeness checks.

Compositions preserve transparent engine, scope, coverage, and confidence
metadata.

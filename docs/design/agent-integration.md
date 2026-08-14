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

The repository ships a portable Agent Skill under `skills/dotnet-axi/`. Agent
Skills tooling can install it for one repository or for the user:

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
regular-expression, stable-syntax, declaration, exact-symbol, and bounded
context discovery and teaches exact
version-pinned `dnx dnaxi@<version> --verbosity quiet -- <command>` invocation
so an agent does not require a permanent global-tool installation. Known
reported routes are invoked directly without a redundant help probe. The
narrowest relevant help is inspected once only when no documented route or
option applies. A verified local or global invocation MAY be used only when
explicitly selected. Guidance treats the
invoked tool's help, version, and capability output as authoritative and never
assumes that a command exists merely because a newer skill mentions it.

The skill is generated from one canonical command-guidance source, with a CI
check that detects stale generated content. Its compact `SKILL.md` entrypoint
carries common file, text, and stable-syntax routes. It links one generated
advanced-evidence reference for declarations, symbol identity, bounded context,
compiler verification, impact, and validation, so simple discovery does not
load unrelated procedure. The complete directory is installed by Agent Skills
tooling and distributed independently from the NuGet tool package. Structured
help and the home view do not repeat skill procedure. They retain command or
workspace facts and provide exact version-pinned invocations only when an
actionable suggestion or recovery path needs one. Generated skills do not
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
- Invoke the documented stable class, catch, invocation, and object-creation
  syntax routes directly. Label their results as syntax candidates rather than
  compiler-verified identity. When a request requires object-creation syntax
  to expose the requested type, include only exact type matches and exclude
  unresolved target-typed `new()` candidates.
- Use a returned path or match to choose the next narrower evidence-producing
  file, text, or syntax query instead of broadly dumping source. A truncated
  result's retrieval command is used only when the omitted rows are needed.
- Search declarations with explicit solution or project scope when a repository
  has multiple entry points, preserve all reported owner and
  configuration/framework variants, and treat the rows as passive candidates
  rather than compiler-verified meaning. Test-only declarations require
  explicit test inclusion.
- Use opt-in syntax verification only when compiler proof is required and
  repository design-time execution is allowed. Preserve the verified,
  rejected, or unresolved status of each owner/framework variant.
- Resolve one selected canonical symbol identity with the same complete search
  scope. Stale or ambiguous identities retain bounded replacement candidates
  and a concrete correction; an agent must select a replacement explicitly
  rather than silently rebinding the old identity.
- Retrieve a bounded symbol, exact document line span, or syntax outline before
  requesting a larger character budget or complete output. Compose declaration,
  owner, document, and outline sections through bounded symbol context when
  those sections are needed together.
- Treat references, callers, callees, tests, implementations, and other graph
  or relationship sections as unavailable until the invoked version reports
  them. The skill names the `0.5.0` context sections without inventing later
  commands or conclusions.
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

The portable skill keeps only agent-neutral command guidance in `SKILL.md` and
its generated references. Host-specific operation belongs to the calling agent
and is not shipped as a skill reference, so Codex, Claude, Grok, and other hosts
apply their own controls without receiving another host's flags as portable
requirements.
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
- Prefer Codex [subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents) for clean-context delegation inside an active Codex turn because they inherit the parent turn's sandbox and live approval overrides.
  Treat standalone `codex exec` as an owning automation boundary, not a sandbox escape: start it only where the host already permits Codex initialization and access to the selected worktree.
  Do not probe a restricted sandbox with a nested launch and then repeat the same worker outside it.

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

`context symbol` is a passive composition: it returns declaration, owner,
document, and outline tool evidence under one caller-selected whole-section
character budget. It does not infer intent or synthesize conclusions. Shared
document and declaration identities make provenance and deduplication explicit,
and every rerun or candidate continuation preserves the effective symbol
workspace scope.

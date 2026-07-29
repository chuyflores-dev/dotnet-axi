# dotnet-axi Requirements

> **Status:** Draft  
> **Version:** 0.2  
> **Date:** 2026-07-29  
> **Working product name:** `dotnet-axi`

## 1. Purpose

`dotnet-axi` is an agent-first command-line interface for understanding, analyzing, validating, and safely modifying .NET codebases.

It combines:

- Fast text discovery for ordinary source searches.
- AST-based structural discovery using AST-grep when available.
- Roslyn syntax and semantic analysis as the authority for C# meaning.
- MSBuild project graph analysis for dependency-aware scoping.
- The official `dotnet` CLI as the authority for SDK operations.
- Claude Code, Codex, OpenCode, or another coding agent as the optional natural-language reasoning layer.

The tool MUST remain useful without embeddings, an LLM API, a vector database, a persistent repository index, or a long-running daemon.

The primary objective is:

> Improve coding-agent task accuracy while lowering total token cost, tool calls, turns, and unnecessary code loading for .NET work.

`dotnet-axi` is not a developer CLI with agent-friendly formatting added later. Its commands, defaults, schemas, errors, context boundaries, and validation workflows MUST be designed around how autonomous coding agents discover evidence, make decisions, edit code, and prove completion.

The product succeeds only when an agent can complete representative .NET engineering tasks more reliably and with less total interaction overhead than baseline use of raw text search, raw `dotnet` output, and unrestricted file reading.

---

## 2. Normative language

The keywords **MUST**, **MUST NOT**, **REQUIRED**, **SHOULD**, **SHOULD NOT**, and **MAY** describe requirement priority.

- **MUST / MUST NOT:** required for conformance.
- **SHOULD / SHOULD NOT:** expected unless a documented reason prevents it.
- **MAY:** optional.

---

## 3. Normative references

The interface requirements are based on:

- [AXI — Agent eXperience Interface](https://github.com/kunchenguid/axi)
- [AXI Skill and detailed interface guidelines](https://github.com/kunchenguid/axi/blob/main/.agents/skills/axi/SKILL.md)
- [TOON — Token-Oriented Object Notation](https://toonformat.dev/)
- [AST-grep documentation](https://ast-grep.github.io/)
- [Tree-sitter documentation](https://tree-sitter.github.io/tree-sitter/)
- [Roslyn workspace APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.workspace)
- [Roslyn SymbolFinder APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder)
- [MSBuild ProjectGraph APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.graph.projectgraph)

Where this document conflicts with a dependency's default CLI behavior, the stable `dotnet-axi` contract takes precedence.

---

## 4. Product decisions

### 4.1 No embeddings

The core product MUST NOT require:

- An embedding model.
- A vector database.
- An external LLM API.
- A locally hosted LLM.
- Precomputed semantic vectors.

The term **semantic** primarily means compiler semantics provided by Roslyn: symbols, types, overload resolution, references, inheritance, implementations, accessibility, and semantic models.

Natural-language conceptual questions are handled by the calling agent through iterative deterministic searches.

### 4.2 No mandatory full index

The tool MUST NOT require a complete repository index before it can answer commands.

A first-time user MUST be able to run text, file, structural, and candidate-scoped symbol searches immediately.

### 4.3 Current worktree is the source of truth

The authoritative state is:

```text
Current files in the active worktree
+ uncommitted changes
+ selected solution/project
+ selected build configuration
+ selected target framework and MSBuild properties
+ installed .NET SDK/MSBuild environment
```

Any cache or retained session state is disposable derived data.

### 4.4 Roslyn is the semantic authority

AST-grep and text search MAY discover candidates, but Roslyn MUST make final decisions for:

- Exact symbol identity.
- Reference resolution.
- Type resolution.
- Overload selection.
- Interface implementation.
- Inheritance and overrides.
- Compiler diagnostics.
- Code fixes.
- Refactorings.
- Safe cross-file modifications.

### 4.5 The official `dotnet` CLI is the SDK authority

`dotnet-axi` MUST wrap rather than reimplement restore, build, test, run, publish, format, template, project, solution, package, tool, and workload operations.

### 4.6 Agent reasoning remains outside the tool

`dotnet-axi` MUST NOT call Claude, Codex, or another model internally.

The agent is responsible for:

- Interpreting conceptual questions.
- Generating search hypotheses.
- Selecting follow-up commands.
- Synthesizing explanations.

`dotnet-axi` is responsible for deterministic evidence, execution, and validation.

### 4.7 Agent task success is the primary product metric

Per-command speed or compactness is not sufficient by itself. Product decisions MUST be evaluated by their effect on complete agent tasks.

The primary outcome is task accuracy, including:

- Finding the correct code or relationship.
- Distinguishing verified facts from candidates and uncertainty.
- Avoiding decisions based on incomplete scope.
- Applying only intended changes.
- Producing passing validation evidence when completion is claimed.

The primary efficiency outcomes are:

- Total input and output tokens consumed by the agent task.
- Number of agent turns.
- Number of tool invocations.
- Wall-clock duration.
- Number of files, projects, and source characters loaded unnecessarily.

An optimization that shortens one response but causes extra discovery calls, retries, ambiguity, or incorrect decisions MUST be treated as a regression.

---

## 5. Goals

The product MUST support the following outcomes.

1. Find relevant C# code without loading or compiling the entire solution.
2. Search by text, syntax shape, symbol, and compiler meaning.
3. Traverse project, type, reference, and call relationships.
4. Analyze compiler, analyzer, structural, and architecture findings.
5. Validate changed code, affected projects, or the full solution.
6. Execute common `dotnet` operations through a stable structured interface.
7. Prepare bounded source context for coding agents.
8. Plan and apply safe Roslyn-based changes.
9. Operate correctly in repositories that change constantly.
10. Minimize agent tool calls, tokens, and ambiguity.
11. Work locally without sending source code to an external service.
12. Support automation, CI, and interactive agent sessions.
13. Provide enough deterministic evidence for an agent to justify each important conclusion without rereading whole files.
14. Collapse predictable multi-step investigations into bounded, task-oriented commands where doing so improves accuracy and reduces round trips.
15. Measure complete agent-task success and cost against realistic baselines.

---

## 6. Non-goals

The initial product is not intended to:

- Replace the .NET SDK or MSBuild.
- Replace Roslyn with Tree-sitter.
- Build a universal multi-language intelligence platform.
- Guarantee complete runtime call graphs in the presence of reflection or dynamic dispatch.
- Decompile arbitrary binary dependencies.
- Provide an IDE user interface.
- Require a persistent graph database, SQLite, or another repository index.
- Provide a model-hosting or embedding service.
- Hide whether a result is syntax-only, semantically verified, partial, or complete.
- Modify source through AST-grep rewrites in the MVP.
- Automatically install hooks, plugins, or external dependencies without explicit user intent.

---

## 7. Users and primary use cases

### 7.1 Coding agents

Claude Code, Codex, OpenCode, and similar tools use `dotnet-axi` to:

- Locate declarations and behavior.
- Verify exact references.
- Understand impact before editing.
- Retrieve bounded context.
- Run builds and tests.
- Validate changes before completion.

### 7.2 Developers

Developers use it to:

- Investigate large unfamiliar solutions.
- Find structural patterns.
- Review architecture violations.
- Run targeted validation.
- Preview and apply refactorings.

### 7.3 CI systems

CI uses it to:

- Validate changed or full solution scope.
- Run architecture rules.
- Export machine-readable artifacts.
- Enforce stable exit-code behavior.

---

## 8. System architecture

```text
Claude Code / Codex / OpenCode / Developer / CI
                         |
                         v
                    dotnet-axi
                         |
                  Query planner
                         |
      +------------------+--------------------+
      |                  |                    |
      v                  v                    v
 Text discovery    Structural discovery    Roslyn
 built-in/rg       AST-grep adapter         syntax + semantics
      |                  |                    |
      +------------------+----------+---------+
                                    |
                                    v
                         MSBuild ProjectGraph
                                    |
                                    v
                          Official dotnet CLI
                                    |
                                    v
                  TOON results + contextual guidance
```

### 8.1 Engine responsibilities

| Engine | Responsibility | Requires full solution load |
|---|---|---:|
| Built-in file catalog | Repository, solution, project, and source-file discovery | No |
| Text engine | Literal and regular-expression search | No |
| Structural engine | AST-shaped candidate discovery | No |
| Roslyn syntax engine | C# syntax inspection and fallback structural queries | No, selected files only |
| Roslyn semantic engine | Exact symbols, types, references, diagnostics, changes | Selected projects |
| MSBuild ProjectGraph | Evaluated project dependency graph | No Roslyn compilation |
| `dotnet` CLI | Restore, build, test, run, publish, format, SDK actions | Operation-dependent |
| Agent | Natural-language intent and iterative reasoning | External |

---

## 9. Progressive analysis model

The tool MUST use progressive analysis rather than mandatory eager indexing.

### Level 0 — Repository catalog

Discover Git/workspace root, branch, worktree changes, solution/project files, `global.json`, `Directory.Build.*`, `Directory.Packages.props`, source paths, and the evaluated project graph. No Roslyn compilation is required.

### Level 1 — Text and syntax discovery

Perform file-name search, literal/regex text search, AST-grep structural search, selected-file Roslyn parsing, and declaration/outline extraction. No semantic compilation is required.

### Level 2 — Candidate-scoped semantics

1. Discover candidate files cheaply.
2. Map candidates to owning projects.
3. Load candidate projects and required dependencies.
4. Use Roslyn to verify exact symbols or relationships.
5. Return verified results with coverage metadata.

### Level 3 — Dependency-aware expansion

Expand into projects that can legally reference the target based on the evaluated project graph for reference, caller, implementation, and impact queries.

### Level 4 — Complete analysis

Full solution analysis occurs only when:

- The user explicitly passes `--complete`.
- A full validation profile is selected.
- A mutation cannot be performed safely with partial coverage.
- Correctness requires complete reference analysis.

---

## 10. Workspace requirements

### WSP-001 — Repository discovery

The tool MUST discover the current Git root or nearest workspace root from the current directory.

### WSP-002 — Solution discovery

The tool MUST detect `.sln`, `.slnx`, and project entry points.

If multiple plausible solutions exist and no deterministic default is available, the command MUST fail non-interactively with the ambiguity, candidate paths, and a concrete `--solution <path>` correction.

### WSP-003 — Configuration selection

The tool MUST support:

- `--configuration <name>`.
- `--framework <tfm>`.
- Repeated `--property <name=value>`.
- `--solution <path>`.
- `--project <path-or-name>`.
- `--path <path>`.
- `--changed`.

### WSP-004 — Project graph

The tool MUST build an evaluated MSBuild project graph without requiring all Roslyn projects to be compiled.

### WSP-005 — Worktree awareness

Commands MUST reflect tracked, untracked, modified, and deleted files plus the current project configuration.

### WSP-006 — Snapshot identity

Every semantic operation and mutation plan MUST be associated with a workspace snapshot identity sufficient to detect relevant source or project changes before applying a plan.

### WSP-007 — Conflicts

When unresolved Git conflicts exist:

- Read-only operations MAY continue on unaffected files.
- Results MUST identify excluded conflicted files.
- Mutation operations MUST be blocked when conflicts affect their scope.

---

## 11. Query planner requirements

### QRY-001 — Cheapest correct engine

The planner MUST choose the least expensive engine capable of satisfying the requested resolution.

### QRY-002 — No implicit full load

Ordinary search commands MUST NOT silently load or compile every project.

### QRY-003 — Explicit resolution

Results MUST declare one of:

- `text`
- `syntax`
- `semantic`
- `verified-partial`
- `complete`

### QRY-004 — Explicit coverage

Partial semantic or graph results MUST include projects considered, projects analyzed, projects remaining when known, and why the result is partial.

### QRY-005 — Complete mode

Commands that support partial discovery SHOULD accept `--complete`.

### QRY-006 — Explainable planning

Commands SHOULD support `--explain-plan` to show the selected engine class, candidate scope, projects expected to load, and whether full solution analysis is required.

### QRY-007 — Scope preservation

Contextual suggestions MUST carry forward fixed scope flags such as `--solution`, `--project`, `--configuration`, and `--framework`.

---

## 12. Search requirements

### 12.1 Text search

#### TXT-001 — Command

```bash
dotnet-axi search text <query>
```

#### TXT-002 — Modes

The command MUST support literal search by default, `--regex`, case-sensitive/insensitive modes, path/project scope, changed-file scope, and explicit generated-code inclusion.

#### TXT-003 — Engine

The tool MAY use `rg` when available but MUST expose a stable `dotnet-axi` contract. A built-in fallback MUST exist.

#### TXT-004 — Output

Default rows SHOULD contain file, line, match preview, and match identifier. Include total count when the scan has already determined it.

#### TXT-005 — Empty result

No matches MUST produce an explicit successful zero-result response and exit code `0`.

### 12.2 Structural search

#### STR-001 — Commands

```bash
dotnet-axi search structural --pattern '<pattern>'
dotnet-axi search structural --rule <rule-id-or-path>
```

#### STR-002 — AST-grep integration

AST-grep SHOULD be the preferred initial structural-search adapter. It MUST:

- Use structured JSON internally.
- Respect repository ignore rules by default.
- Support include/exclude globs.
- Support cancellation.
- Translate results into stable internal contracts.
- Keep progress and diagnostics out of stdout.

#### STR-003 — AST-grep absence

When AST-grep is unavailable:

- Built-in structural queries MUST fall back to Roslyn syntax where implemented.
- Raw backend-specific patterns MAY fail with an actionable structured error.
- Unrelated commands MUST remain functional.

#### STR-004 — Verification

```bash
dotnet-axi search structural --pattern '<pattern>' --verify
```

Verification MUST discover candidates, map them to projects, load only candidate scope when possible, resolve requested types/symbols with Roslyn, and report discovered, verified, rejected, and unresolved counts.

#### STR-005 — Confidence

Syntax-only matches MUST NOT be described as compiler-confirmed facts.

#### STR-006 — No-match exit translation

AST-grep may return a non-zero exit code for no matches. The adapter MUST translate a valid no-match condition into explicit zero results and `dotnet-axi` exit code `0`.

#### STR-007 — Rewrites

AST-grep rewrite operations MUST NOT directly modify user files in the MVP.

### 12.3 Stable syntax search

The product SHOULD expose tool-owned queries that do not require AST-grep syntax:

```bash
dotnet-axi search syntax invocation --name SaveChangesAsync
dotnet-axi search syntax class --attribute Authorize
dotnet-axi search syntax object-creation --type HttpClient
dotnet-axi search syntax catch --type Exception --empty
```

The implementation MAY use AST-grep, Roslyn syntax, or both, but user-facing semantics MUST remain stable.

### 12.4 Symbol search

#### SYM-001 — Command

```bash
dotnet-axi search symbol <query>
```

#### SYM-002 — Deterministic matching

Matching SHOULD rank exact fully qualified names, exact identifiers, case-insensitive exact matches, prefixes, camel-case/token matches, substrings, and optionally documentation/declaration text.

#### SYM-003 — Filters

Support `--kind`, `--namespace`, `--project`, `--path`, `--accessibility`, `--include-tests`, and `--include-generated` where applicable.

#### SYM-004 — Candidate-first resolution

Identify candidate declarations before loading semantic projects.

#### SYM-005 — Result identity

Results MUST include an entity ID usable by subsequent commands. IDs MUST be valid for the associated snapshot and SHOULD remain stable during a warm session while the declaration is unchanged.

#### SYM-006 — Default fields

Default symbol rows SHOULD include only identifier, kind, name, and location. Additional fields MUST be available through `--fields`.

### 12.5 Compiler-semantic search

The product MUST support:

```bash
dotnet-axi search references <symbol>
dotnet-axi search implementations <symbol>
dotnet-axi search overrides <symbol>
dotnet-axi search derived <symbol>
dotnet-axi search callers <symbol>
dotnet-axi search callees <symbol>
```

#### SEM-001 — Exact symbol

Semantic commands MUST resolve a specific symbol first. Ambiguity MUST return candidates and a concrete correction using a symbol ID or fully qualified name.

#### SEM-002 — Project graph narrowing

Reference and caller searches MUST avoid projects that cannot reference the target.

#### SEM-003 — Partial mode

The default MAY return verified partial results for responsiveness.

#### SEM-004 — Complete mode

`--complete` MUST perform the complete relevant static scope.

#### SEM-005 — Mutation safety

Deletion, rename, change-signature, and similar planning MUST NOT rely on partial reference results.

#### SEM-006 — Runtime uncertainty

Distinguish directly resolved calls, possible virtual/interface targets, inferred convention-based links, and runtime-unknown relationships.

---

## 13. Show, outline, and context requirements

### SHW-001 — Show symbol

```bash
dotnet-axi show symbol <symbol>
```

Return declaration identity, signature, containing project/type, source location, documentation preview, body preview when applicable, and cheap relationship summaries.

### SHW-002 — Show document

```bash
dotnet-axi show document <path>
```

Default to an outline and bounded preview rather than dumping a large file.

### SHW-003 — Outline

```bash
dotnet-axi outline <path-or-symbol>
```

Return imports, namespace, types, members, signatures, and relevant attributes. AST-grep outline MAY be used; Roslyn syntax MUST be a fallback.

### CTX-001 — Bounded context

```bash
dotnet-axi context symbol <symbol> \
  --include declaration,callers,callees,tests \
  --max-chars 12000
```

The command MUST enforce an explicit or configured output budget.

### CTX-002 — Truncation

When context is truncated, include actual included size, total known size, omitted sections, and a concrete `--full` or larger-budget command.

### CTX-003 — Deterministic ordering

Repeated calls against an unchanged snapshot MUST use deterministic ordering.

### CTX-004 — Evidence-oriented context

Context bundles MUST preserve source locations, symbol identities, resolution level, workspace snapshot identity, and relationship provenance so an agent can distinguish tool evidence from its own inference.

### CTX-005 — Token-cost visibility

Context responses SHOULD report actual character count and an estimated token range. The estimate MUST be labeled approximate because tokenizer behavior varies by agent and model.

### CTX-006 — No redundant context

A context bundle SHOULD avoid repeating the same declaration or source span through multiple relationships. Shared evidence SHOULD be emitted once and referenced by stable ID where practical.

---

## 14. Graph requirements

### GRF-001 — Project graph

Support:

```bash
dotnet-axi graph projects
dotnet-axi graph dependencies <project>
dotnet-axi graph cycles
```

### GRF-002 — Code graph model

Recommended nodes: solution, project, document, namespace, type, member, test, diagnostic, package.

Recommended edges: contains, declares, references, calls, constructs, inherits, implements, overrides, reads, writes, project-reference, package-reference, tests.

### GRF-003 — On-demand edges

The MVP MUST build code edges on demand rather than precomputing a complete graph.

### GRF-004 — Commands

The tool SHOULD support callers, callees, implementations, derived types, path, and impact queries.

### GRF-005 — Coverage

Graph responses MUST declare whether they are syntax candidates, semantically verified partial results, or complete within the statically knowable scope.

### GRF-006 — Impact

Impact output SHOULD summarize affected projects, documents, candidate tests, public-surface impact, important relationship paths, and confidence.

### GRF-007 — No graph database

The graph API MUST NOT depend on Neo4j, SQLite, or another persistent graph store in the MVP.

---

## 15. Static analysis requirements

### ANA-001 — Compiler analysis

Expose current compiler diagnostics for selected documents, projects, affected scope, or solution.

### ANA-002 — Analyzer integration

Run analyzers already configured by the repository. Additional analyzer packs MAY be opt-in.

### ANA-003 — Structural rules

Support AST-grep YAML rules for syntax-only policies such as empty catch blocks, direct `HttpClient` construction, forbidden syntax, and migration patterns.

### ANA-004 — Architecture rules

Support configuration-driven checks such as forbidden project/namespace dependencies, layer boundaries, circular dependencies, and infrastructure types exposed by public APIs.

### ANA-005 — Findings model

A normalized finding MUST include code/rule ID, severity, message, location, source engine, and confidence/resolution when relevant.

### ANA-006 — Deduplication

Equivalent findings from multiple engines SHOULD be merged or linked without hiding their source.

### ANA-007 — Heuristic honesty

Possible dead code and convention-based relationships MUST be labeled as candidates.

### ANA-008 — Changed scope

```bash
dotnet-axi analyze changed
```

MUST analyze changed files and the minimal affected project scope possible.

---

## 16. Validation requirements

### VAL-001 — Command

```bash
dotnet-axi validate --profile <fast|standard|full>
```

### VAL-002 — Fast profile

SHOULD include workspace verification, changed-document parsing, affected-project compilation when available, compiler diagnostics, configured analyzers for affected scope, optional format verification, and configured fast structural checks.

### VAL-003 — Standard profile

SHOULD include restore when required, affected builds and dependents, compiler/analyzer diagnostics, format verification, architecture rules, and affected/configured tests.

### VAL-004 — Full profile

SHOULD include full restore, solution build/tests/analyzers/format, architecture checks, configured package/vulnerability policy, public API checks, and publish checks.

### VAL-005 — Configurable profiles

Repository configuration MUST be able to add, remove, or reorder checks.

### VAL-006 — Result summary

Precompute overall status, passed/failed/skipped/warning counts, duration per check, top failures, and analyzed scope.

### VAL-007 — Exit behavior

- Passing validation: exit `0`.
- Validation failure: exit `1`.
- Invalid command usage: exit `2`.

### VAL-008 — Failure continuation

Support `--continue-on-error` for collecting independent failures.

### VAL-009 — Raw logs

Raw `dotnet`, test, or analyzer logs MUST NOT flood stdout. Provide a concise summary plus a local artifact path or explicit `--full` retrieval mechanism.

---

## 17. `dotnet` operation requirements

### DOT-001 — First-class commands

Provide structured adapters for restore, build, test, run, publish, format, new, project, solution, package, tool, and workload operations.

### DOT-002 — Escape hatch

```bash
dotnet-axi exec -- dotnet <arguments>
```

Everything after `--` is pass-through input.

### DOT-003 — No silent argument dropping

First-class commands MUST reject unknown flags. Pass-through arguments are accepted only after `--`.

### DOT-004 — Noninteractive execution

Wrapped commands MUST suppress or avoid prompts. Missing required input MUST produce a structured actionable error.

### DOT-005 — Structured result

Results SHOULD include operation, exit code, duration, scope, summary, failure count, and log artifact location.

### DOT-006 — Output translation

Translate SDK output into actionable `dotnet-axi` results. Raw output MAY be retained in an artifact but MUST NOT replace the structured response.

### DOT-007 — Cancellation

Long-running SDK operations MUST honor cancellation and terminate child processes.

---

## 18. Safe modification requirements

### MOD-001 — Read-only default

Search, graph, analysis, and validation commands MUST NOT modify source files.

### MOD-002 — Plan then apply

```bash
dotnet-axi refactor rename --symbol <symbol> --to <name>
dotnet-axi apply <plan-id>
```

The first command creates a plan and MUST NOT write source files.

### MOD-003 — Plan contents

A plan MUST include operation, workspace snapshot, affected documents/references, diff summary, required validation level, and plan ID.

### MOD-004 — Stale plan protection

Before apply, verify relevant source/project files still match the plan snapshot. Reject stale plans with a concrete regeneration command.

### MOD-005 — Roslyn changes

Cross-file semantic changes MUST be calculated through Roslyn.

### MOD-006 — Idempotence

An already satisfied change SHOULD produce a successful no-op with exit `0`.

### MOD-007 — Validation

Applied changes MUST run the requested validation profile.

### MOD-008 — Initial post-MVP changes

Prioritize symbol rename, registered Roslyn code fixes, missing using/import fixes, and formatting. Change-signature and move-type MAY follow after correctness benchmarks.

---

## 19. Agent integration requirements

### AGT-001 — Explicit setup

Hooks/plugins MUST only be installed by user-invoked commands:

```bash
dotnet-axi setup claude-code
dotnet-axi setup codex
dotnet-axi setup opencode
```

### AGT-002 — Default targets

Support Claude Code, Codex, and OpenCode rather than hard-coding one agent.

### AGT-003 — Session start

A session-start integration SHOULD inject a compact, directory-scoped home view.

### AGT-004 — Directory scope

Ambient context MUST only describe the current workspace.

### AGT-005 — Token budget

Ambient context MUST be smaller than an ordinary explicit query result and exclude deep source content.

### AGT-006 — Path verification and repair

Setup MUST prefer the PATH-resolved executable when correct, fall back to an absolute path, repair outdated paths, and be idempotent.

### AGT-007 — Skill generation

Ship an installable Agent Skill as a secondary discovery path. Generate it from the same guidance source as the CLI home view, and provide a CI stale-content check.

### AGT-008 — No transcript capture by default

Do not collect full agent transcripts. A future opt-in feature MAY retain only tool-owned metadata such as commands run, tool-modified files, and validation outcomes.

### AGT-009 — Suggested agent behavior

Generated guidance SHOULD teach agents to use text search for literals, structural search for syntax shape, Roslyn operations for exact identity, impact before public changes, bounded context, fast validation during work, and standard validation before completion.

### AGT-010 — Evidence-first responses

Agent-facing results MUST include the minimum evidence required to support the next decision: source location, entity identity, engine/resolution, scope coverage, and confidence or uncertainty where applicable.

The tool MUST NOT produce authoritative natural-language conclusions that exceed the evidence returned by Roslyn, MSBuild, AST-grep, Git, analyzers, tests, or the `dotnet` CLI.

### AGT-011 — Single-call sufficiency

When a predictable follow-up is cheap and broadly required for correct interpretation, the command SHOULD include a precomputed summary rather than forcing another invocation. Examples include total counts, verified/rejected counts, affected-project counts, test status, validation status, truncation size, and remaining coverage.

### AGT-012 — Task-oriented compositions

The CLI SHOULD provide bounded composite commands for common agent tasks when composition lowers errors or round trips. Examples include:

- Structural discovery plus Roslyn verification.
- Symbol context plus selected callers, callees, and tests.
- Changed-file analysis plus affected-project validation.
- Mutation planning plus required completeness checks.

Composite commands MUST preserve transparent engine, scope, and coverage metadata.

### AGT-013 — Agent-neutral contract

Claude Code, Codex, OpenCode, and future agents MUST use the same deterministic CLI contracts. Agent-specific setup MAY differ, but command semantics and evidence models MUST NOT depend on hidden prompts or one model's behavior.

### AGT-014 — Completion evidence

Generated agent guidance MUST instruct agents not to claim a code change is complete solely because files were edited. Completion SHOULD be backed by the strongest applicable `dotnet-axi validate` result available within the requested scope.

---

## 20. AXI interface conformance

### AXI-001 — TOON stdout

All normal agent-facing stdout MUST use TOON. Internal components SHOULD use typed objects and convert at the output boundary.

### AXI-002 — Minimal default schemas

Collection rows MUST default to approximately three or four fields. Additional fields MUST be opt-in through `--fields`.

### AXI-003 — Content truncation

Large text MUST include a useful preview, total known size, truncation notice, and `--full` or larger-budget escape hatch. Default previews SHOULD generally remain between 500 and 1,500 characters.

### AXI-004 — Precomputed aggregates

Include cheap aggregates that prevent predictable follow-ups: total matches, verified/rejected counts, projects considered/loaded, validation counts, and affected project/test counts.

### AXI-005 — Definitive empty states

A valid query with no results MUST explicitly state zero results and exit `0`.

### AXI-006 — Structured errors

Errors MUST be emitted to stdout in the same structured format and include a stable code, actionable message, and concrete correction.

### AXI-007 — Output channels

- **stdout:** structured data, errors, suggestions.
- **stderr:** debug logs, progress, dependency diagnostics.
- **exit 0:** success, empty result, no-op.
- **exit 1:** operation or validation failure.
- **exit 2:** usage error.

Progress MUST NOT appear on stdout.

### AXI-008 — No interactive prompts

Every operation MUST be expressible entirely through flags and arguments.

### AXI-009 — Unknown input

Unknown commands, arguments, and flags MUST fail before dependency execution. Usage errors MUST exit `2`, identify the invalid input, inline valid flags or concise help, and provide renamed-flag guidance when known.

### AXI-010 — Content-first home view

Running `dotnet-axi` with no arguments MUST show live workspace state, not a general manual.

Include executable path with `~`, one-sentence description, workspace path, selected solution/project, cheap project/source counts, changed-file count, cheap diagnostic status, and a few contextual suggestions.

### AXI-011 — Contextual disclosure

Discovery and mutation responses SHOULD include a few relevant complete commands/templates, preserve fixed scope flags, use placeholders for runtime values, and omit suggestions when the result is self-contained.

### AXI-012 — Help

Every subcommand MUST support concise `--help` with required arguments, flags/defaults, and two or three examples.

### AXI-013 — Version and update

Support `--help`, `-v`, `--version`, and preferably a reserved `update` command.

### AXI-014 — Stable evidence references

List and summary responses SHOULD return stable IDs that let the agent request detail without repeating long names, paths, signatures, or source content.

### AXI-015 — Output budget controls

Commands that can return unbounded collections or source content MUST support appropriate limits such as `--limit`, `--fields`, `--max-chars`, `--max-depth`, and `--full`. Defaults MUST be chosen to solve common tasks in one call without dumping repository-scale content.

### AXI-016 — No token-only optimization

Schema or truncation changes MUST NOT be accepted solely because they reduce output size. They MUST also preserve or improve task success, error recovery, and the agent's ability to determine whether results are complete.

---

## 21. Example output contracts

### 21.1 Home

```toon
bin: ~/.dotnet/tools/dotnet-axi
description: Search, analyze, validate, and safely change the current .NET workspace
workspace:
  root: ~/src/credit-platform
  solution: CreditPlatform.slnx
  projects: 142
  csharp_files: 18400
git:
  branch: feature/renewal-rules
  changed_files: 17
analysis:
  status: not_loaded
  compiler_errors: unknown
help[3]:
  Run `dotnet-axi search symbol "<name>"`
  Run `dotnet-axi analyze changed`
  Run `dotnet-axi validate --profile fast`
```

### 21.2 Structural search

```toon
resolution: syntax
count: 3
matches[3]{id,file,line,construct}:
  ast_01,src/Orders/OrderRepository.cs,84,invocation
  ast_02,src/Payments/PaymentRepository.cs,112,invocation
  ast_03,tests/DbFixture.cs,39,invocation
help[2]:
  Run `dotnet-axi show document <path>`
  Run `dotnet-axi search structural --pattern "<pattern>" --verify`
```

### 21.3 Verified search

```toon
resolution: semantic
discovered: 17
verified: 11
rejected: 4
unresolved: 2
matches[11]{id,kind,name,location}:
  sym_01,method,DbContext.SaveChangesAsync,src/Orders/OrderRepository.cs:84
  ...
```

### 21.4 Explicit empty state

```toon
matches: 0 symbols found for "LegacyPaymentRule" in CreditPlatform.slnx
```

Exit code: `0`.

### 21.5 Usage error

```toon
error:
  code: usage.unknown_flag
  message: Unknown flag `--stat` for `search symbol`
valid_flags[6]:
  --kind
  --project
  --path
  --include-tests
  --include-generated
  --help
```

Exit code: `2`.

### 21.6 Partial graph

```toon
resolution: verified-partial
target: CreditEvaluator.EvaluateAsync
projects:
  considered: 14
  analyzed: 6
  remaining: 8
callers[4]{id,name,location,confidence}:
  sym_21,CreditEndpoint.Handle,src/Api/CreditEndpoint.cs:31,verified
  ...
help[1]:
  Run `dotnet-axi graph callers sym_42 --complete`
```

### 21.7 Validation

```toon
status: failed
profile: standard
duration_ms: 18439
checks[6]{name,status,errors,warnings}:
  workspace,passed,0,0
  restore,passed,0,0
  build,failed,2,11
  analyzers,failed,1,19
  architecture,passed,0,0
  tests,skipped,0,0
failures[3]{code,message,location}:
  CS8602,Possible null dereference,src/Rules/RuleEvaluator.cs:73
  CS1503,Argument type mismatch,src/Api/Endpoints.cs:28
  ARCH001,Domain references Infrastructure,src/Domain/Domain.csproj
```

---

## 22. Performance and scalability requirements

### PRF-001 — First useful response

The tool MUST provide useful commands before any full repository semantic analysis completes.

### PRF-002 — No startup index

The home view, text search, file search, and structural search MUST NOT wait for a full semantic index.

### PRF-003 — Candidate-first semantics

Semantic operations MUST attempt to load only candidate projects and required dependencies.

### PRF-004 — On-demand graph

Code graph edges MUST be computed on demand in the MVP.

### PRF-005 — Warm reuse

Within one process or optional session, reuse loaded MSBuild state, Roslyn snapshots, parsed syntax trees, confirmed resolutions, and graph edges where safe.

### PRF-006 — Optional daemon

A later daemon MAY keep workspaces warm, but the CLI MUST work without it, foreground queries MUST outrank background warming, and every operation MUST verify current source state.

### PRF-007 — Cancellation and limits

Expensive commands MUST support cancellation and SHOULD support `--timeout`, `--limit`, `--max-projects`, `--max-depth`, and `--max-chars`.

### PRF-008 — Benchmark targets

The project MUST include a repeatable large-repository benchmark. Initial targets on a documented modern workstation with NVMe and a reference repository of approximately 50,000 C# files are:

| Operation | Cold P95 target |
|---|---:|
| Home view | ≤ 2 seconds |
| File/text search | ≤ 5 seconds |
| Repository-wide AST structural search | ≤ 15 seconds |
| Candidate semantic verification of up to 5 projects | ≤ 15 seconds |
| Repeated warm symbol query | ≤ 3 seconds |

Targets may be adjusted only through published benchmark evidence.

### PRF-009 — Full-operation visibility

Commands intentionally performing full analysis MUST clearly state their scope in stderr progress and final structured output.

### PRF-010 — Agent task efficiency

Performance work MUST measure complete task trajectories, not only isolated command latency. A faster command that increases retries, turns, or incorrect conclusions is not an improvement.

### PRF-011 — Progressive result usefulness

When a complete operation is expensive, the command MAY return a clearly labeled verified-partial result only when that result is independently useful and cannot be mistaken for complete evidence. Mutations and destructive decisions MUST wait for required completeness.

---

## 23. Freshness, cache, and concurrency requirements

### FRS-001 — Cache optionality

Deleting all `dotnet-axi` state MUST never change correctness, only performance.

### FRS-002 — MVP persistence

The MVP SHOULD avoid a mandatory persistent repository database.

### FRS-003 — Optional future cache

Any future persistent cache MUST be local, worktree-scoped, disposable, content-addressed where practical, excluded from Git, schema-versioned, and verified against the active worktree.

### FRS-004 — Branch and working changes

Cache validity MUST NOT depend only on Git commit. Uncommitted and untracked source changes MUST be included.

### FRS-005 — Project configuration changes

Changes to `*.csproj`, `*.props`, `*.targets`, solutions, `global.json`, `Directory.Build.*`, `Directory.Packages.props`, `NuGet.config`, or `.editorconfig` MUST reload affected MSBuild/Roslyn state.

### FRS-006 — Public-surface awareness

A future cache SHOULD distinguish implementation changes from public API surface changes.

### FRS-007 — Concurrency

A future daemon/cache MUST use one logical writer per worktree, allow multiple readers, and isolate independent Git worktrees.

### FRS-008 — Stale operations

Read operations MAY return a coherent captured snapshot. Mutations MUST revalidate relevant files immediately before apply.

---

## 24. Configuration requirements

### CFG-001 — Repository configuration

Read an optional root-level `dotnet-axi.yml`.

### CFG-002 — Supported defaults

Configuration MAY define default solution/configuration/framework/properties, ignored/generated paths, test patterns, validation profiles, architecture rules, structural rule directories, output limits, and performance limits.

### CFG-003 — Example

```yaml
workspace:
  solution: CreditPlatform.slnx
  configuration: Debug
  framework: net10.0

search:
  exclude:
    - "**/bin/**"
    - "**/obj/**"
    - "**/*.g.cs"
  includeGeneratedByDefault: false
  defaultLimit: 100

structural:
  ruleDirectories:
    - .dotnet-axi/rules

validation:
  profiles:
    fast:
      - compiler
      - analyzers:changed
      - structural:fast
    standard:
      - restore:affected
      - build:affected
      - analyzers:affected
      - architecture
      - tests:affected
    full:
      - restore:solution
      - build:solution
      - analyzers:solution
      - architecture
      - tests:solution
      - packages

architecture:
  layers:
    - name: Domain
      projects: ["*.Domain"]
    - name: Application
      projects: ["*.Application"]
    - name: Infrastructure
      projects: ["*.Infrastructure"]
  rules:
    - from: Domain
      cannotReference: Infrastructure
```

### CFG-004 — Invalid configuration

Invalid configuration MUST report file location, property path, actionable correction, and exit `2`.

---

## 25. Security and privacy requirements

### SEC-001 — Local-first

Source analysis MUST occur locally.

### SEC-002 — No model calls

The tool MUST NOT transmit source code to an embedding, LLM, or AI API.

### SEC-003 — Network behavior

Network access MUST only occur when explicitly required by the selected operation, such as restore, update, workload, or package queries.

### SEC-004 — Telemetry

Product-specific telemetry MUST be disabled by default.

### SEC-005 — Process safety

External processes MUST be launched without shell string concatenation. Use argument-list APIs.

### SEC-006 — Secret handling

Do not echo environment variables, credentials, tokens, or full command environments.

### SEC-007 — Hook consent

Agent hooks/plugins MUST be explicit opt-in and idempotently removable.

### SEC-008 — Source writes

Source writes MUST be limited to explicit apply or SDK mutation commands.

---

## 26. Platform and packaging requirements

### PKG-001 — Runtime

The initial implementation SHOULD target .NET 10 and C#.

### PKG-002 — Platforms

Support Windows, macOS, and Linux.

### PKG-003 — Distribution

Primary distribution SHOULD be a .NET global/local tool:

```bash
dotnet tool install --global dotnet-axi
```

### PKG-004 — SDK selection

Respect repository `global.json` and selected SDK/MSBuild context.

### PKG-005 — Optional accelerators

- Text search MUST have a built-in implementation; `rg` MAY accelerate it.
- Core syntax queries MUST have a Roslyn implementation; AST-grep SHOULD accelerate and expand structural capabilities.
- Direct Tree-sitter embedding is deferred until benchmarks justify it.

### PKG-006 — Dependency discovery

Missing optional accelerators MUST produce concise capability information rather than breaking unrelated commands.

### PKG-007 — Version compatibility

Report `dotnet-axi` version, selected SDK, relevant Roslyn/MSBuild compatibility, and structural-engine availability.

---

## 27. Internal component requirements

Recommended structure:

```text
src/
  DotNetAxi.Cli/
  DotNetAxi.Axi/
  DotNetAxi.Workspaces/
  DotNetAxi.Search/
  DotNetAxi.Structural/
  DotNetAxi.Roslyn/
  DotNetAxi.Graph/
  DotNetAxi.Analysis/
  DotNetAxi.Validation/
  DotNetAxi.DotNet/
  DotNetAxi.Changes/
  DotNetAxi.Contracts/
```

### CMP-001 — Stable contracts

Adapters MUST return stable internal contracts rather than exposing raw dependency schemas.

### CMP-002 — Replaceable engines

The design SHOULD provide replaceable interfaces such as:

```csharp
public interface ITextSearchEngine;
public interface IStructuralSearchEngine;
public interface IWorkspaceProvider;
public interface ISemanticSearchEngine;
public interface IGraphService;
public interface IDotNetCommandRunner;
public interface IValidationCheck;
```

### CMP-003 — AST-grep adapter

The initial structural adapter SHOULD invoke AST-grep as an external process. Direct Tree-sitter integration MAY later implement the same contract.

### CMP-004 — Output boundary

Only the CLI/output layer SHOULD depend on TOON serialization. Business logic MUST operate on typed result objects.

---

## 28. Testing requirements

### TST-001 — Unit tests

Cover command parsing, unknown flags, exit mapping, TOON serialization, truncation, empty states, scope selection, query planning, and dependency translation.

### TST-002 — Integration fixtures

Include fixture repositories for single/multi-project solutions, multi-targeting, conditional compilation, generated code, cycles, analyzers, tests, uncommitted changes, ambiguous solutions, broken projects, and Git conflicts.

### TST-003 — Structural correctness

Test AST-grep candidates against Roslyn verification for representative patterns.

### TST-004 — Roslyn correctness

Compare references and relationships against direct Roslyn API results.

### TST-005 — Mutation safety

Prove plan does not write, stale plans are rejected, apply changes only planned files, validation runs after apply, and repeated satisfied intent is a no-op where appropriate.

### TST-006 — Golden output

Use golden/snapshot tests to prevent accidental schema bloat.

### TST-007 — Cross-platform tests

Test path handling, executable discovery, and child-process cancellation on supported systems.

### TST-008 — Performance harness

Measure cold start, text/structural search, candidate semantics, references/callers, changed/full validation, warm reuse, and agent tokens/turns where practical.

### TST-009 — Agent task benchmark

The repository MUST include a repeatable benchmark harness where supported coding agents complete representative .NET tasks against controlled repositories.

The benchmark MUST capture:

- Task success and correctness.
- Unsupported or unjustified claims.
- Input tokens and output tokens.
- Agent turns and tool invocations.
- Wall-clock duration.
- Files and projects inspected.
- Whether required validation was executed and passed.

### TST-010 — Baseline comparisons

At minimum, compare `dotnet-axi` against a documented baseline using ordinary file reads, `rg` or equivalent text search, and raw `dotnet` commands. Additional comparisons MAY include Roslyn-oriented MCP servers or other code-intelligence tools.

### TST-011 — Benchmark task categories

Benchmark scenarios SHOULD include declaration lookup, exact-reference lookup, conceptual behavior discovery, caller/impact analysis, diagnostic investigation, targeted code change, safe rename, architecture-rule detection, changed-scope validation, and multi-project failure diagnosis.

### TST-012 — Agent-experience regression gate

Changes to output schemas, defaults, suggestions, context construction, or command composition SHOULD fail CI when benchmark evidence shows a material task-success regression or a material increase in median tokens, turns, or tool calls without a documented accuracy benefit.

### TST-013 — Release-level agent outcome gate

For a release to claim an agent-experience improvement, the representative benchmark suite MUST demonstrate both:

- Equal or higher aggregate task success than the documented raw-tool baseline, with no material regression on safety-critical tasks.
- Measurably lower median total token consumption across complete task trajectories.

Turn count, tool-call count, duration, and validation completion MUST be published as supporting metrics. Individual tasks MAY spend more tokens only when the result gains measurable correctness, completeness, or safety and the tradeoff is documented.

---

## 29. MVP scope

The MVP MUST include:

### Interface

- TOON output.
- Structured errors.
- Exit codes `0`, `1`, and `2`.
- No interactive prompts.
- Strict flag validation.
- No-arguments home view.
- Contextual suggestions.
- Concise help.
- Version reporting.

### Workspace

- Workspace/solution discovery.
- Project graph.
- Git/worktree summary.
- Configuration/framework selection.

### Search

- File and text search.
- AST-grep structural search when available.
- Stable built-in syntax queries.
- Symbol declaration search.
- Show symbol/document.
- Outline and bounded context.

### Semantics and graph

- References.
- Implementations.
- Overrides.
- Derived types.
- Project dependencies and cycles.
- Callers/callees for supported cases.
- Explicit partial versus complete coverage.

### Analysis and validation

- Compiler diagnostics.
- Configured Roslyn analyzers.
- Structural rules.
- Basic architecture rules.
- Fast and standard validation profiles.

### SDK execution

- Restore.
- Build.
- Test.
- Format.
- `exec --` escape hatch.

### Agent integration

- Explicit Claude Code setup.
- Explicit Codex setup.
- Generated Agent Skill.
- Idempotent path repair.
- Evidence-first agent guidance.
- Validation-backed completion guidance.

### Agent-experience proof

- A repeatable benchmark harness.
- Baseline trajectories using ordinary search, file reads, and raw `dotnet` commands.
- Measurement of task success, tokens, turns, tool calls, duration, and validation completion.
- Golden tasks demonstrating that compact output does not hide required evidence or completeness.

The MVP MUST NOT require SQLite, a graph database, embeddings, a model API, a full solution index, a daemon, direct Tree-sitter bindings, or source mutation.

---

## 30. Post-MVP scope

### Phase 2

- Safe rename plan/apply.
- Registered Roslyn code fixes.
- Full validation profile.
- Package/vulnerability checks.
- Improved impact and affected-test analysis.
- OpenCode integration.
- Optional warm session process.

### Phase 3 — benchmark-gated

Only implement with benchmark evidence:

- Direct Tree-sitter integration.
- Persistent content-addressed syntax cache.
- Persisted confirmed graph edges.
- CI-produced immutable baseline snapshots.
- Advanced architectural graph rules.
- Additional safe refactorings.

Embeddings remain outside the core roadmap unless introduced as a separate optional extension with no effect on deterministic operation.

---

## 31. Acceptance criteria

The initial release is acceptable when:

1. `dotnet-axi` produces a live TOON home view without compiling the entire solution.
2. Text search works without a persistent index.
3. Structural search works through AST-grep when installed.
4. AST-grep no-match is translated to explicit zero results and exit `0`.
5. Symbol search discovers candidates and verifies only owning projects where possible.
6. Results distinguish syntax, semantic, partial, and complete coverage.
7. Project graph commands work without loading all Roslyn compilations.
8. References and implementations use exact Roslyn symbols.
9. Fast validation analyzes changed/affected scope without mandatory full-solution work.
10. Build and test output is summarized in TOON rather than dumped raw.
11. Unknown flags fail before dependency execution with exit `2`.
12. Agent-facing errors are structured on stdout.
13. Progress/debug data never pollutes stdout.
14. No command prompts interactively.
15. Current uncommitted source changes are reflected.
16. No embeddings, model calls, vector storage, SQLite, or daemon are needed.
17. Claude Code and Codex setup are explicit, idempotent, and directory-scoped.
18. Performance benchmarks verify that first useful searches do not wait for full semantic analysis.
19. The CLI passes conformance tests for all ten AXI principles.
20. Deleting all tool state does not change correctness.
21. Representative agent benchmarks show equal or higher aggregate task success than the raw-tool baseline, with no material regression on safety-critical tasks.
22. Representative agent benchmarks show measurably lower median total token use across complete task trajectories.
23. Any individual benchmark task that uses more tokens documents a measurable correctness, completeness, or safety benefit.
24. Search and graph results include enough provenance for an agent to distinguish verified facts, candidates, partial coverage, and inference.
25. Agent guidance requires validation evidence before claiming applicable implementation tasks complete.
26. Output-schema regression tests prevent token growth that does not improve task success.

---

## 32. Deferred decisions

The following may be finalized during implementation without changing the architecture:

- Final product/package name.
- Exact TOON .NET serializer.
- Whether AST-grep is user-installed, bundled as a platform sidecar, or optionally provisioned by setup.
- Exact user-cache directories.
- Stable symbol ID encoding.
- Self-update implementation for a .NET global tool.
- Affected-test heuristic.
- Reference benchmark repository and hardware specification.

These decisions MUST preserve the requirements in this document.

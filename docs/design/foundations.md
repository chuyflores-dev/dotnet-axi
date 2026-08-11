# Design Foundations

This document defines the authorities, boundaries, and evidence model shared
by every `dotnet-axi` capability.

## Normative language

The keywords **MUST**, **MUST NOT**, **REQUIRED**, **SHOULD**, **SHOULD NOT**,
and **MAY** describe design priority:

- **MUST / MUST NOT:** required for conformance.
- **SHOULD / SHOULD NOT:** expected unless a documented reason prevents it.
- **MAY:** optional.

Command examples and explanatory text inherit the priority of the rule that
introduces them. Text explicitly marked **informative** does not define
conformance. The MVP boundary in [REQUIREMENTS.md](../../REQUIREMENTS.md)
determines which rules apply to the initial release.

## Normative references

The interface design is based on these pinned or vendor-authoritative
references:

- [AXI — Agent eXperience Interface, commit `d5aa171`](https://github.com/kunchenguid/axi/tree/d5aa171665bb784d0f1b05150aaeb0f3e1b52b2f)
- [AXI Skill, commit `d5aa171`](https://github.com/kunchenguid/axi/blob/d5aa171665bb784d0f1b05150aaeb0f3e1b52b2f/.agents/skills/axi/SKILL.md)
- [TOON Specification v4.1](https://github.com/toon-format/spec/blob/v4.1.0/SPEC.md)
- [Tree-sitter documentation](https://tree-sitter.github.io/tree-sitter/)
- [Roslyn workspace APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.workspace)
- [Roslyn SymbolFinder APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder)
- [MSBuild ProjectGraph APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.graph.projectgraph)

TOON v4.1 is the wire format for output schema `dotnet-axi/v1`. A later TOON
version MUST NOT be adopted without conformance tests, golden-output review,
and an output-schema compatibility decision.

Where this design conflicts with a dependency's default CLI behavior, the
stable `dotnet-axi` contract takes precedence.

AXI is design guidance rather than a formal wire protocol. The MVP has two
documented deviations:

1. OpenCode setup is deferred while Claude Code and Codex setup ship in the
   MVP.
2. `dotnet-axi` does not capture agent transcripts. This privacy boundary
   takes precedence over lifecycle-capture guidance.

The product MUST describe itself as **AXI-aligned with documented deviations**
until those deviations are removed or accepted by a newer pinned AXI profile.

## Product boundaries

### No embeddings or internal model calls

The core product MUST NOT require an embedding model, vector database,
external or local LLM, precomputed semantic vectors, or model API.

Within `dotnet-axi`, **semantic** means compiler semantics provided by Roslyn:
symbols, types, overload resolution, references, inheritance,
implementations, accessibility, and semantic models. The calling agent handles
natural-language interpretation and iterative reasoning.

### No mandatory full index

The tool MUST NOT require a complete repository index before answering
commands. File, text, syntax, and candidate-scoped symbol searches MUST be
available on first use.

### Current worktree authority

The authoritative state is:

```text
Current files in the active worktree
+ uncommitted changes
+ selected solution/project
+ selected build configuration
+ selected target framework and MSBuild properties
+ installed .NET SDK/MSBuild environment
```

Caches and retained session state are disposable derived data.

### Semantic and SDK authorities

Text and syntax search MAY discover candidates, but Roslyn MUST decide exact
symbol identity, references, types, overloads, implementations, inheritance,
compiler diagnostics, code fixes, refactorings, and safe cross-file changes.

The official `dotnet` CLI is the SDK authority. `dotnet-axi` MUST wrap rather
than reimplement restore, build, test, run, publish, format, template, project,
solution, package, tool, and workload operations.

### Agent and tool responsibilities

The external agent is responsible for interpreting conceptual questions,
forming search hypotheses, selecting follow-ups, and synthesizing
explanations. `dotnet-axi` is responsible for deterministic evidence,
execution, and validation.

Primary consumers are:

- Coding agents locating behavior, verifying relationships, retrieving bounded
  context, running checks, and proving completion.
- Developers investigating unfamiliar solutions, syntax shapes,
  architecture violations, validation scope, and refactorings.
- CI systems validating changed or complete scope, enforcing architecture
  rules, exporting structured artifacts, and relying on stable exit behavior.

## Evidence model

Every evidence-bearing result MUST represent these dimensions independently:

- **Resolution:** `text`, `syntax`, or `semantic`.
- **Coverage:** `not-applicable`, `partial`, or `complete`.
- **Confidence:** `candidate`, `verified`, `heuristic`, or `unknown`.
- **Scope:** the selected solution, projects, frameworks, configuration, and
  the portion actually analyzed.

`complete` means complete only within the declared statically knowable scope.
It MUST NOT imply knowledge of reflection, dynamic loading, runtime-generated
code, external services, or other behavior outside that scope.

Product changes MUST be evaluated by complete agent-task accuracy and cost,
not isolated response size or command latency. An optimization that causes
extra discovery calls, retries, ambiguity, or incorrect conclusions is a
regression even if one response is smaller.

## Passive and executing operations

Every command MUST be classified as:

- **Passive:** repository catalog, file/text/syntax search, project evaluation,
  and semantic inspection that does not intentionally execute repository build
  targets, tests, configured analyzers, or source generators.
- **Executing:** restore, build, test, run, publish, format apply, configured
  analyzer execution, source-generator execution, SDK mutations, or another
  operation capable of executing repository- or dependency-provided code.

Classification is typed policy data registered before command parsing or
validation planning. Each policy independently declares network access,
repository-code execution, artifact writes, metadata writes, user-state
writes, and source writes. Registries reject missing policies and contradictory
combinations. In particular, passive operations cannot access the network,
execute repository code, or write source, and validation checks cannot write
source under either classification.

Executing a command is explicit consent for the selected operation to run
repository or dependency code with the caller's operating-system permissions.
Help and final output MUST disclose the classification. Session-start
integrations, the home view, and ordinary discovery commands MUST remain
passive.

## System architecture

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
 Text discovery    Structural queries      Roslyn
 built-in/rg       Roslyn syntax            syntax + semantics
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

| Engine | Responsibility | Full solution load |
|---|---|---:|
| Built-in catalog | Repository, solution, project, and source discovery | No |
| Text engine | Literal and regular-expression search | No |
| Structural query layer | Stable tool-owned C# syntax candidates | No |
| Roslyn syntax engine | C# parsing and structural queries | No; selected files only |
| Roslyn semantic engine | Exact symbols, relationships, diagnostics, and changes | Selected projects |
| MSBuild ProjectGraph | Evaluated project dependencies | No Roslyn compilation |
| `dotnet` CLI | SDK operations | Operation-dependent |
| Agent | Natural-language intent and iterative reasoning | External |

Business logic MUST operate on typed result objects. Only the CLI/output layer
SHOULD depend on TOON serialization. Engine contracts MUST allow text,
structural, syntax, semantic, graph, and SDK adapters to evolve independently.
Capability components provide typed query-plan candidates through
`DotNetAxi.Contracts`; `DotNetAxi.Analysis` selects a plan, and the CLI only
renders it.

Most capability projects depend only on `DotNetAxi.Contracts`. The semantic
Roslyn engine additionally composes the SDK host resolver, structural
candidate contracts, and workspace/MSBuild authority; the CLI remains the
only project that composes every capability.

## Progressive analysis

The tool MUST use progressive analysis instead of mandatory eager indexing.

### Level 0 — Repository catalog

Discover the Git/workspace root, branch and worktree changes, solution/project
files, `global.json`, `Directory.Build.*`, `Directory.Packages.props`, and
source paths. The home view MUST NOT evaluate the full MSBuild project graph.

### Level 1 — Text and syntax discovery

Perform file-name search, literal/regex search, stable Roslyn syntax queries,
declarations/outlines, and on-demand project-graph evaluation. No Roslyn
compilation is required.

### Level 2 — Candidate-scoped semantics

1. Discover candidate files cheaply.
2. Map candidates to owning projects.
3. Load candidate projects and required dependencies.
4. Use Roslyn to verify exact symbols or relationships.
5. Return verified results with coverage metadata.

Candidate-scoped syntax verification evaluates workspace project declarations
to derive effective `Compile` membership, including imports, properties, and
globs. It then loads only the owning configuration/framework variants with the
SDK selected for the workspace. Failed projects retain passive ownership as
explicit unresolved coverage. Because the Roslyn MSBuild loader can run
repository design-time targets, selecting `--verify` is an executing operation
even though ordinary syntax search stays passive.
Missing project inputs, metadata, or compiler meaning remain unresolved
coverage; they are not repaired, restored, or inferred by the semantic layer.

### Level 3 — Dependency-aware expansion

Expand into projects that can legally reference the target based on the
evaluated project graph for reference, caller, implementation, and impact
queries.

### Level 4 — Complete analysis

Full-solution analysis occurs only when the user passes `--complete`, selects a
full validation profile, requests a mutation that cannot be safe with partial
coverage, or correctness otherwise requires complete reference analysis.

If complete analysis requires repository code execution, the command MUST be
classified as executing or fail with an actionable request for an explicit
executing operation or `--allow-repository-code`. It MUST NOT silently
downgrade a requested complete result.

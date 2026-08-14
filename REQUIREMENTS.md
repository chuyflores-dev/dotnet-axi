# dotnet-axi Requirements

## Product

`dotnet-axi` is an agent-first command-line interface for understanding,
analyzing, validating, and safely modifying .NET codebases.

The repository and product are named `dotnet-axi`. Starting with 0.4.0, the
.NET tool package and installed command are named `dnaxi`; the earlier
`dotnet-axi` package ID remains the immutable 0.2.0 and 0.3.0 distribution.

Its purpose is to improve coding-agent accuracy while reducing total tokens,
tool calls, turns, and unnecessary code loading compared with raw file reads,
text search, and `dotnet` output.

The tool provides deterministic evidence and execution. Natural-language
reasoning remains the responsibility of the calling agent.

## Required outcomes

`dotnet-axi` must enable an agent or developer to:

- Discover the active .NET workspace and its current worktree state.
- Find code by path, text, syntax shape, declaration, and compiler meaning.
- Inspect exact references, implementations, inheritance, and supported call
  relationships.
- Understand project dependencies and likely change impact.
- Retrieve bounded source context without reading entire repositories.
- Analyze compiler, analyzer, structural, and architecture findings.
- Validate changed, affected, or complete solution scope.
- Run common official `dotnet` operations through a stable structured
  interface.
- Plan and apply safe Roslyn-based changes after the MVP.
- Distinguish verified facts from candidates, uncertainty, and incomplete
  coverage.
- Operate locally without sending source code to an AI service.
- Demonstrate improved complete-task outcomes through repeatable benchmarks.

## Product constraints

The product:

- Does not require embeddings, an LLM API, vector storage, a graph database, a
  persistent repository index, or a daemon.
- Treats the current worktree and selected build context as authoritative.
- Uses Roslyn as the authority for C# semantics.
- Uses MSBuild evaluation for project structure and the official `dotnet` CLI
  for SDK operations.
- Uses progressive, candidate-first analysis rather than mandatory full
  solution loading.
- Reports resolution, coverage, scope, and applicable confidence
  independently.
- Keeps passive discovery separate from operations that can execute repository
  code, access the network, or write files.
- Produces machine-readable TOON v4.1 using output schema `dotnet-axi/v1`.
- Remains usable when Git, `rg`, restore assets, or some projects are
  unavailable, with explicit capability or coverage reporting.

The product is not intended to replace the .NET SDK, MSBuild, Roslyn, an IDE,
or the calling agent's reasoning. It does not promise complete runtime
knowledge in the presence of reflection, dynamic dispatch, generated runtime
code, or external systems.

## MVP

The MVP includes:

- Passive home, workspace, solution, project, framework, and changed-scope
  discovery.
- File, text, stable Roslyn syntax, and symbol declaration search.
- Stateless evidence IDs plus bounded show, outline, and context retrieval.
- Roslyn-backed references, implementations, overrides, derived types, and
  supported callers/callees.
- On-demand project dependency, cycle, path, and impact analysis.
- Compiler, configured analyzer, structural-rule, and basic architecture
  analysis.
- Fast and standard validation profiles.
- Structured restore, build, test, format check/apply, and constrained raw
  `dotnet` execution.
- Explicit Claude Code and Codex repository setup, generated Agent Skill
  guidance, repair, and removal.
- Deterministic output, compatibility, security, performance, and agent-task
  test harnesses.

The MVP does not include general source refactoring/apply, OpenCode setup, a
warm daemon, direct Tree-sitter bindings, or a persistent semantic/code graph.
Explicit `format --apply` remains an SDK mutation.

## Release bar

The MVP is acceptable when:

- Passive commands perform no restore, telemetry, repository-code execution,
  or other tool-initiated network access.
- Workspace selection, changed scope, multi-targeting, broken projects, and
  unsupported inputs remain explicit and actionable.
- Search works without persistent state and behaves consistently with and
  without optional accelerators.
- Semantic and graph results match Roslyn/MSBuild authority within their
  declared scope and never disguise partial coverage as complete.
- Entity IDs resolve across fresh processes and cannot silently bind to a
  different declaration.
- SDK and validation commands are noninteractive, cancellable, structured, and
  preserve dependency exit information within the public `0/1/2` exit
  contract.
- Normal stdout, including errors, strict-decodes as TOON v4.1 under
  `dotnet-axi/v1`; progress and raw dependency diagnostics stay on stderr or in
  protected artifacts.
- Configuration, platform, SDK, adapter, locale, network, process, secret, and
  artifact safety tests pass on the published compatibility matrix.
- The deterministic performance fixture meets the documented cold P95 targets
  on the designated reference runner.
- A candidate agent canary completes every release-critical repository task
  with an allowed diff and passing deterministic validation. Any `dnaxi`
  diagnostic is retained, while an unrecovered tool problem is observed as a
  task failure; final-answer wording is not graded.
- A named agent-experience improvement claim uses a paired raw-tool comparison
  to show equal or higher verified task success, no safety-critical regression,
  and at least 10% lower median total tokens and wall-clock duration for the
  tested agent, model, and harness.
- Generated agent guidance requires applicable validation evidence before
  claiming completion.

## Beyond the MVP

The next phase may add safe rename and Roslyn code fixes, full validation,
package/vulnerability policy, improved affected-test analysis, OpenCode setup,
warm sessions, and additional first-class SDK adapters.

Persistent syntax caches, confirmed graph edges, immutable CI snapshots,
advanced architecture rules, and additional refactorings require benchmark
evidence. Embeddings remain outside the core roadmap unless introduced as an
optional extension that does not affect deterministic operation.

## Detailed design

The accepted behavioral and architectural details live under
[docs/](docs/README.md). Those references define command grammar, evidence,
output schemas, failure behavior, safety boundaries, compatibility, and test
protocols.

Repository work items define bounded delivery scope. GitHub Issues track live
status, and pull requests update the relevant design references whenever
accepted behavior changes.

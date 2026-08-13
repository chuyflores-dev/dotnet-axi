# Workspace Design

This document defines how `dotnet-axi` finds and identifies repositories,
solutions, projects, configurations, frameworks, and changed scope.

## Repository discovery

The tool MUST discover the current Git root from the current directory. Outside
Git, it MUST choose the nearest ancestor containing `dotnet-axi.yml`, a
solution, or a supported project file, in that order. If no marker exists, it
MUST use the current directory and report `workspace_kind: directory`.
An ancestor that the caller cannot read is a discovery boundary: the tool MUST
stop ascending there and continue deterministically from the current directory
instead of failing or attempting to widen the caller's access.

## Solution and project selection

The tool MUST detect `.sln`, `.slnx`, and supported project entry points.
Solution filters (`.slnf`), file-based C# applications, and unsupported project
types MUST be reported as capabilities rather than silently treated as fully
supported inputs.

Selection precedence is:

1. Explicit `--solution` or `--project`.
2. Repository configuration.
3. The single solution in the workspace root.
4. The single supported project in the workspace root.

If multiple candidates remain, the command MUST fail non-interactively with
the ambiguity, candidate paths, and a concrete `--solution <path>` or
`--project <path>` correction.

Passive symbol commands share this selection contract. `search symbol`, `show
symbol`, and symbol-targeted `outline` resolve the same effective entry point,
project set, explicit paths, and source eligibility. A directory workspace
with no supported entry point remains searchable, but the commands never pick
one entry point when multiple candidates have equal precedence. Repository
configuration participates through the existing configured-selector hook once
configuration loading supplies it; the symbol commands do not define a second
configuration schema.

Selecting a solution reads its `.sln` or `.slnx` membership passively without
evaluating projects or loading a compilation. Selecting a project uses that
single passive owner. Traversal is restricted to files owned by the selected
projects, including literal linked `Compile` sources already recognized by
passive ownership; explicitly selected external paths remain available and
visibly unowned. Repeated solution members, overlapping project directories,
and linked files are de-duplicated by normalized file identity while every
distinct owner and compiler-variant candidate remains attached.

Workspace-aware commands MUST support the applicable selectors:

- `--configuration <name>`.
- `--framework <tfm>`.
- Repeated `--property <name=value>`.
- `--solution <path>`.
- `--project <path-or-name>`.
- `--path <path>`.
- `--changed`.
- `--base <git-ref>`.
- `--head <git-ref>`.

## Evaluated project graph

Commands requiring dependency information MUST build an evaluated MSBuild
project graph without requiring every Roslyn project to compile. Commands that
do not need dependency information, including the home view, MUST NOT pay this
cost.

Graph evaluation starts from the solution or project chosen by workspace
selection and passes configuration, target framework, and repeated explicit
MSBuild properties as global properties. Repeated properties use the last
value; the dedicated configuration and framework selectors take precedence
over conflicting generic properties.

The graph uses the installed SDK's MSBuild `ProjectGraph` authority without
running targets, restoring, or loading Roslyn. The official PATH-selected
`dotnet` host resolves repository `global.json` policy before the matching SDK
instance is registered. An incompatible process-wide registration returns a
typed compatibility failure instead of evaluating with a different authority.
Authority probes have bounded time and output and start in owned process
containment, so completion, failure, or cancellation cannot leave descendants
behind even when the original host process has already exited.

Project paths and dependency edges are deterministic and slash-separated;
external projects use paths relative to the workspace and carry an external
marker. When native relative paths cannot cross storage roots, a non-rooted
`../.external-volume/<root-hash>/...` identity distinguishes roots without
emitting an absolute root. Project link targets are authorized before node
evaluation, and an implicit directory-link escape fails visibly. Failed
evaluation, circular dependencies, and missing restore assets prevent complete
graph coverage and remain attached to visible project nodes or the graph as
stable typed reasons. Cycle reasons apply only to actual cycle participants.
When MSBuild is unavailable, known solution members remain visible as failed
nodes and the solution file itself is never represented as a project.

## Worktree awareness

Commands MUST reflect tracked, staged, unstaged, untracked, renamed, and
deleted files together with the selected project configuration.

Without `--base`, `--changed` means staged, unstaged, untracked, renamed, and
deleted paths relative to `HEAD`.

With `--base <ref>` and no `--head`, it means changes from
`merge-base(<ref>, HEAD)` through the current commit plus current worktree
changes. With both `--base` and `--head`, it means the committed three-dot diff
`<base>...<head>` and excludes unrelated ambient worktree changes. Results MUST
report resolved commits and whether worktree changes were included.

Outside Git, `--changed` MUST fail with exit `2` and a structured
`workspace.git_required` correction.

When unresolved Git conflicts exist:

- Read-only operations MAY continue on unaffected files.
- Results MUST identify excluded conflicted files.
- Mutations MUST be blocked when conflicts affect their scope.

## Snapshot identity

Every semantic operation and mutation plan MUST use a content-derived workspace
snapshot identity. The identity MUST cover the declared scope and every
observed input that can affect the result, including:

- Selected source and linked documents, additional files, and analyzer
  configuration.
- Solution/project files and every evaluated MSBuild import, including external
  imports.
- `global.json`, selected SDK/MSBuild/Roslyn versions, configuration, target
  framework, runtime identifier, and explicit MSBuild properties.
- The selected solution or project kind and canonical workspace-relative path,
  without the selector provenance that led to the same selection.
- `NuGet.config`, NuGet lock/assets files, metadata-reference bytes,
  generated-source inputs, and generator/analyzer identities when those
  components execute.
- Relevant Git conflict and working-tree state.

Results MUST disclose captured scope. A snapshot ID MUST NOT claim to represent
files or frameworks the operation did not observe.

## Multi-targeting

If a project targets multiple frameworks and `--framework` is absent, a
semantic command MAY select the first declared compatible framework for
responsiveness, but it MUST report partial framework coverage. `--complete`
MUST analyze every compatible declared framework that can change the answer.
Mutation planning MUST use complete framework coverage.

An explicit framework not supported by a selected project MUST produce a
structured usage error instead of silently falling back.

Before semantic project evaluation, passive declaration discovery may expose
candidate compiler variants from literal `Configurations`, `TargetFramework`,
and `TargetFrameworks` values in the owning project file. It hashes the complete
project-file bytes into the variant context, performs no restore or MSBuild
execution, and emits an additional unspecified candidate whenever any declared
configuration or framework value is absent or partly unevaluated. It does not
drop that unknown portion merely because another literal value is available.
Evaluated semantic commands remain authoritative for declaration meaning,
supported framework selection, and complete coverage.

## Project and language support

The MVP MUST guarantee semantic analysis for SDK-style C# projects loadable by
a supported installed .NET SDK. Unsupported, non-C#, non-SDK-style, or
Visual-Studio-only projects MUST remain visible in discovery and graph output
with a capability status. They MUST NOT be silently omitted or described as
semantically analyzed.

SDK execution MAY operate on additional project types when the selected
official `dotnet` command supports them.

## Paths and locations

Input paths resolve relative to the current directory unless documented
otherwise. Output paths MUST be normalized workspace-relative paths using `/`
as the separator. Source locations MUST use one-based lines and one-based
UTF-16 columns. Backend-specific zero-based or byte offsets MAY be exposed only
as opt-in fields.

Passive commands MUST NOT follow directory symlinks by default. A path that
escapes the selected workspace through a symlink MUST require explicit path
scope and be identified as external.

## Restore and broken projects

Passive discovery and semantic commands MUST NOT perform implicit restore or
other network operations. Missing assets, SDKs, workloads, references, and
broken projects MUST produce partial or failed coverage with an actionable
`dnaxi restore` or scope correction.

Projects that fail to load MUST remain in coverage counts with a stable reason;
they MUST NOT disappear from the denominator.

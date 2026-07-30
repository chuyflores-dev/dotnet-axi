# Runtime and Distribution Design

This document defines freshness, configuration, security, packaging, platform
compatibility, and internal component boundaries.

## Freshness and cache

Deleting all `dotnet-axi` state MUST never change correctness—only
performance. The MVP avoids a mandatory persistent repository database.

Any future persistent cache is local, worktree-scoped, disposable,
content-addressed where practical, excluded from Git, schema-versioned, and
verified against the active worktree.

Exact user-cache and artifact directory names follow platform conventions and
are implementation choices subject to the isolation and security rules below.

Cache validity MUST include uncommitted and untracked content rather than
depending only on a Git commit. Changes to any observed semantic input reload
affected MSBuild/Roslyn state, including:

- Source and linked files.
- Additional files and analyzer configuration.
- Generated-source inputs.
- Project and solution files.
- Evaluated `.props` and `.targets`.
- `global.json` and `Directory.Build.*`.
- `Directory.Packages.props` and `NuGet.config`.
- Package lock/assets files.
- `.editorconfig`.
- Selected SDK/workload identity.
- Explicit MSBuild properties.

A future cache SHOULD distinguish implementation changes from public API
surface changes. A future daemon/cache uses one logical writer per worktree,
allows multiple readers, and isolates independent Git worktrees.

Read operations MAY return a coherent captured snapshot. Mutations revalidate
relevant files immediately before apply.

Cache keys include output schema, tool and adapter versions, selected
workspace/configuration/framework properties, and content identities for every
represented input. Modification time alone never proves freshness.

Deleting cache/state leaves passive results and entity-ID resolution
semantically unchanged, though recalculation MAY be slower. Explicit mutation
plans are not disposable cache.

## Repository configuration

The tool reads an optional root-level `dotnet-axi.yml`. After selecting the
workspace root, it does not search for alternate filenames or parent
configuration.

Configuration MAY define:

- Default solution, configuration, framework, and MSBuild properties.
- Ignored and generated paths.
- Test patterns.
- Validation profiles.
- Architecture rules.
- Structural rule directories.
- Output limits.
- Performance limits.

Configuration schema `version: 1` accepts:

```yaml
version: 1

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

Invalid configuration reports file location, property path, an actionable
correction, and exits `2`. Unknown keys or validation checks, duplicate layer
names, cyclic profile references, and unsupported schema versions are errors
rather than ignored input.

Precedence is:

1. Explicit command-line flags.
2. `dotnet-axi.yml`.
3. Repository-derived values such as `global.json`.
4. Tool defaults.

`--explain-plan` reports effective values and their source. Conflicting
repeated CLI properties fail with exit `2`; identical repeats MAY be
deduplicated.

Configured paths resolve relative to the configuration file. External paths
must be explicit, labeled external, and cannot broaden ambient context.

The required integer `version` controls configuration compatibility. Backward
incompatible changes increment it and provide a structured migration error or
command.

Validation checks declare repository-code execution, network access, and
artifact writes. Configuration cannot place source-writing operations in a
validation profile. Invalid or unavailable checks are reported before any
profile step executes.

## Security and privacy

Source analysis occurs locally. The tool does not transmit source to an
embedding, LLM, or other AI API.

### Network and telemetry

Network access occurs only when explicitly required by the selected operation,
such as restore, update, workload, or package queries. Passive commands do not
restore, vulnerability-check, update, or download workload advertising
manifests.

Repository code executed by a selected operation may attempt network access
outside the wrapper's control unless operating-system sandboxing is active.
Help and results disclose this limitation.

Product telemetry is disabled by default. Child `dotnet` processes opt out of
.NET CLI telemetry. Any future explicit telemetry opt-in describes fields and
destinations and excludes source, paths, symbols, arguments, and transcripts.

### Process and secret safety

External processes use argument-list APIs without shell string concatenation.
They receive a controlled working directory, bounded output capture,
cancellation, timeout, and process-tree termination. User-controlled source
text is never interpolated into a shell command.

The tool does not echo complete environments, credentials, tokens,
authorization headers, or known secret-bearing arguments. Structured output
redacts detected secrets without claiming arbitrary repository logs are
secret-free.

Repository paths, source previews, diagnostics, test names, and dependency
messages are untrusted data encoded through the TOON boundary, never raw-string
escape hatches.

### Repository-code execution

Commands capable of running MSBuild targets, tests, applications, configured
analyzers, source generators, templates, tools, workloads, or package scripts
are classified as executing. The home view, setup hook, file/text/syntax
search, and passive project catalog never trigger repository-code execution.

Executing commands run with the caller's operating-system permissions and say
they are not a security sandbox unless an enforced sandbox is active.

Source writes are limited to explicit apply or SDK mutation commands.

### Diagnostic artifacts

Raw logs and diagnostic artifacts:

- Live outside the repository by default.
- Use randomized tool-owned directories with user-only permissions where
  supported.
- Reject symlink/reparse-point substitution before creation.
- Have a documented retention period, defaulting to seven days.
- Have an explicit cleanup command.
- Are labeled `may_contain_sensitive_data: true` unless produced by a proven
  redacting formatter.

Binary logs that may capture environment or imported project content are
opt-in.

### Setup

Agent integration is explicit, idempotently removable, and atomic. Setup and
removal report exact scope and target paths, preserve unrelated configuration,
and do not alter trust databases, bypass hook review, or weaken managed policy.

## Public names

The public names intentionally separate project identity from invocation:

| Surface | Name |
|---|---|
| Repository and product | `dotnet-axi` |
| .NET tool package | `dotnet-axi` |
| Installed command | `dnaxi` |
| Output schema | `dotnet-axi/v1` |
| Configuration file | `dotnet-axi.yml` |
| Disposable state directory | `.dotnet-axi/` |

Documentation uses `dnaxi` for executable examples. Package installation still
uses `dotnet-axi`, and schema, configuration, and state names do not follow the
short command name.

## Platform and packaging

The implementation SHOULD target .NET 10 and C#. The MVP supports current
GitHub-hosted Windows, macOS, and Linux runner images and publishes the exact
tested OS/RID matrix for each release.

Primary distribution SHOULD be a .NET global/local tool:

```bash
dotnet tool install --global dotnet-axi
```

On .NET 10 or later, one-shot `dnx` execution is the non-persistent,
`npx`-style path:

```bash
dnx dotnet-axi@<version> -- --version
```

Version milestones and external package publication follow the explicit
[release and versioning policy](releases.md).

Direct global invocation (`dnaxi`), local-tool invocation
(`dotnet tool run dnaxi`), and one-shot `dnx` execution expose the same CLI
contract. Setup records an invocation valid for the selected installation and
repairs it idempotently if the installation moves.

The framework-dependent package targets `net10.0` with platform-neutral tool
assets, command name `dnaxi`, Apache-2.0 metadata, repository/readme metadata,
and a portable `.snupkg`. Its application and pinned runtime dependencies are
contained in the tool package; Git and optional search engines are not install
prerequisites.

Package verification may create an ignored local artifact, inspect both
archives, and install or execute them from isolated temporary stores. This is
continuous verification, not publication, and follows the release-policy
credential boundary.

Self-update behavior is not fixed by the MVP design; normal .NET tool update
mechanisms remain sufficient.

The tool respects repository `global.json`, including roll-forward and
prerelease policy, and the selected SDK/MSBuild context. It uses the official
PATH-resolved `dotnet` unless the user selects another supported host path.

### Required and optional dependencies

The packaged tool's .NET runtime is the only universal runtime prerequisite.
Git MAY be absent in non-Git workspaces; Git-only features then return
capability errors.

- Text search always has a built-in implementation; `rg` MAY accelerate it.
- Core syntax queries always have a Roslyn implementation; AST-grep SHOULD
  accelerate and expand structural search.
- Direct Tree-sitter embedding is deferred until benchmarks justify it.

Missing optional accelerators return concise capability information without
breaking unrelated commands.

AST-grep MAY be user-installed, bundled as a platform sidecar, or provisioned
by an explicit setup action. Distribution choice does not change the adapter
contract or make it a universal prerequisite.

Version and home output report `dotnet-axi` version, output schema, selected
SDK, relevant Roslyn/MSBuild compatibility, Git availability, and optional
engine availability.

### Compatibility baseline

The MVP guarantees its passive semantic contract for SDK-style C# projects
that load under installed .NET 8, .NET 9, or .NET 10 SDKs. The tool itself
SHOULD target .NET 10.

Each release publishes tested SDK feature bands and Roslyn/MSBuild versions.
Untested newer SDKs are labeled unverified. If the selected SDK cannot be
loaded safely in-process, the command uses a compatible isolated helper or
returns a structured compatibility error; it does not continue with mismatched
MSBuild assemblies and authoritative claims.

Build-time package versions are pinned. Release evidence records runtime
versions tested for .NET SDK, MSBuild, Roslyn, Git, `rg`, AST-grep, and its C#
grammar. Unsupported optional engines degrade to built-in behavior where
possible.

## Internal components

The intended component structure is:

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

`DotNetAxi.Contracts` has no implementation-project references. Each other
non-CLI component depends only on `DotNetAxi.Contracts`; dependencies on
capabilities are expressed through those stable contracts. `DotNetAxi.Cli` is
the composition root and may reference every component. Test projects may
reference only the production projects they exercise.

Adapters return stable internal contracts rather than raw dependency schemas.
Replaceable boundaries SHOULD include:

```csharp
public interface ITextSearchEngine;
public interface IStructuralSearchEngine;
public interface IWorkspaceProvider;
public interface ISemanticSearchEngine;
public interface IGraphService;
public interface IDotNetCommandRunner;
public interface IValidationCheck;
```

The initial structural adapter invokes AST-grep as an external process. Direct
Tree-sitter integration MAY later implement the same contract.

Only the CLI/output layer depends on TOON. Business logic operates on typed
result objects.

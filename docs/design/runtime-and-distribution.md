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

The passive CLI composition cannot start child processes. Optional capability
probes and Git inspection receive rejecting process guards, text search uses
the built-in engine, and a process-dependent selector returns a typed policy
denial with a non-executing correction. Missing tools or assets never broaden
the operation into process or network access.

### Process and secret safety

External processes use argument-list APIs without shell string concatenation.
They receive a controlled working directory, bounded output capture,
cancellation, timeout, and process-tree termination. User-controlled source
text is never interpolated into a shell command.

Process results distinguish a missing or inaccessible working directory from
an executable start failure. A termination request is not termination evidence:
the terminated lifecycle requires bounded confirmation that the owned process
or containment has exited; otherwise the lifecycle is termination-failed.
Expected stream and containment failures are returned as typed runner outcomes
with any available exit evidence and bounded output.

Windows containment uses a job object and does not complete until the job has
no active processes. Linux and macOS launch a new process group, retain the
unreaped leader identity while terminating and observing its members, and do
not report terminated until the owned group and output handles are clear.
Portable POSIX process groups are not a security sandbox: repository code that
deliberately creates a new session or process group can leave that authority.
Callers requiring enforcement against hostile repository code must supply an
operating-system sandbox; the runner does not claim to prevent that escape.

The tool does not echo complete environments, credentials, tokens,
authorization headers, or known secret-bearing arguments. Structured output
redacts detected secrets without claiming arbitrary repository logs are
secret-free.

Repository paths, source previews, diagnostics, test names, and dependency
messages are untrusted data encoded through the TOON boundary, never raw-string
escape hatches.

### Constrained host failures

The tool reports the restriction it can observe without claiming to identify an agent sandbox that the operating system did not expose.
Observable process-start denial, filesystem permission denial, host-reported network-policy denial, timeout, cancellation, output overflow, and ordinary dependency failure remain distinct typed causes.
An ordinary dependency network error remains a dependency failure unless the host supplies authoritative policy evidence.
Original dependency exit information and bounded diagnostics are preserved when available.

No adapter retries by broadening permissions, changing the active checkout, moving repository writes into a temporary directory, or enabling network.
Results instead identify the blocked operation and path or destination when safe, then provide a correction that requires the caller or user to change the host boundary explicitly.

Every terminal path is bounded.
A child that exits while a descendant retains an output handle cannot stall result collection, and process-group or job cleanup cannot later target a reused unrelated process identity.
Timeout cleanup waits for bounded process and stream wrappers while continuing to fault-observe underlying operations that ignore cancellation without extending the terminal bound.
Tool-owned temporary artifacts remain task-scoped; they are not an escape hatch for a read-only repository.

### Repository-code execution

Commands capable of running MSBuild targets, tests, applications, configured
analyzers, source generators, templates, tools, workloads, or package scripts
are classified as executing. The home view, setup hook, file/text/syntax
search, and passive project catalog never trigger repository-code execution.

Passive command handlers are composed without executing dependencies. Where a
shared service requires a process-shaped interface, the composition supplies a
guard that returns typed not-started evidence and never delegates to the
operating system. Passive commands do not substitute restore, project
evaluation, analyzers, or generators when coverage inputs are unavailable.

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
dnx dotnet-axi@<version> --verbosity quiet -- --version
```

Version milestones and external package publication follow the explicit
[release and versioning policy](releases.md).

Direct global invocation (`dnaxi`), local-tool invocation
(`dotnet tool run dnaxi`), and one-shot `dnx` execution expose the same CLI
contract. Setup records an invocation valid for the selected installation and
repairs it idempotently if the installation moves.

The canonical command vectors are:

| Installation | Command vector before operation arguments |
|---|---|
| Global tool | `dnaxi` |
| Local manifest | `dotnet tool run dnaxi --` |
| One-shot | `dnx dotnet-axi@<version> --verbosity quiet --` |

Package verification gives global and local installs separate temporary CLI
homes and package caches, compares representative structured output across all
three vectors, updates persistent installs in place, and uninstalls them.
One-shot execution never modifies either persistent installation.
Quiet host verbosity is required for one-shot execution so cold NuGet
acquisition messages cannot contaminate structured stdout.

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
- Core C# syntax queries use the in-process Roslyn implementation.
- External structural-search engines and direct Tree-sitter embedding are
  deferred until benchmark evidence justifies them.

Missing optional accelerators return concise capability information without
breaking unrelated commands.

Version and home output report `dotnet-axi` version, output schema, selected
SDK, relevant Roslyn/MSBuild compatibility, Git availability, and optional
engine availability.

Capability reporting keeps availability (`present`, `missing`, or `unverified`)
separate from compatibility (`supported`, `unsupported`, or `unverified`). It identifies the
exact selected `dotnet` host and reports command-engine routing independently;
for example, `search text` selects compatible `rg` when possible and always
reports the built-in degradation path. Detection is limited to bounded
`dotnet --info`, `git --version`, and `rg --version` probes plus passive
assembly-metadata reads. It never installs, updates, restores, downloads,
evaluates a project, or executes repository code.

PATH host discovery ignores empty and relative entries and rejects executable
candidates lexically or physically within the workspace, including symlink or
reparse-point aliases. The same workspace-trust boundary applies to an
explicit host path before any passive version process starts. A timed-out or
failed SDK probe is unverified rather than missing and remains distinct from a
completed host probe that reports no selected SDK.

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
versions tested for .NET SDK, MSBuild, Roslyn, Git, `rg`, supported operating
systems, and runtime identifiers. Unsupported optional engines degrade to
built-in behavior where possible.

The initial tested SDK feature band is stable `10.0.3xx`; other parseable .NET
8-or-newer SDKs are retained as present but labeled unverified, while pre-.NET
8 selections are unsupported. Selected MSBuild and Roslyn assembly versions
inherit that SDK compatibility only when their passive metadata probes
succeed. Git 2.11 or newer within major version 2 is supported; older Git is
unsupported and newer major versions are unverified. `rg` major versions 13
through 15 are supported for optional acceleration; older versions are
unsupported and newer versions are unverified. Missing, malformed, or failed
optional probes do not break commands with a built-in path.

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

The MVP structural-query implementation uses in-process Roslyn syntax. An
external structural engine MAY be considered later only behind stable
product-owned semantics and supporting benchmark evidence.

Only the CLI/output layer depends on TOON. Business logic operates on typed
result objects.

# Analysis and Execution Design

This document defines static analysis, validation profiles, and structured
execution of official .NET SDK commands.

## Static analysis

The MVP exposes these canonical commands:

```bash
dnaxi analyze compiler
dnaxi analyze analyzers
dnaxi analyze structural
dnaxi analyze architecture
dnaxi analyze changed
```

Each command supports the common workspace scope flags applicable to its
engine.

### Compiler diagnostics

`analyze compiler` exposes current compiler diagnostics for selected documents,
projects, affected scope, or solution.

### Configured analyzers

`analyze analyzers` runs analyzers already configured by the repository.
Additional analyzer packs MAY be opt-in.

Configured analyzers and source generators are repository/dependency code.
Commands that run them are executing operations, enforce cancellation and
timeouts, and disclose component identities when available.

Passive semantic commands MUST NOT execute configured analyzers. When a
complete semantic answer requires generated source, the command requires
explicit `--allow-repository-code` consent or an executing composite command.
Without consent, coverage remains partial and identifies generators not run.

Analyzer or generator crashes, timeouts, and load failures become structured
findings or coverage failures. They MUST NOT crash the CLI, corrupt another
snapshot, or be reported as successful analysis.

### Structural and architecture rules

`analyze structural` runs configured tool-owned Roslyn syntax rules for
policies such as empty catches, direct `HttpClient` construction, forbidden
syntax, and migration patterns.

`analyze architecture` runs configuration-driven rules for forbidden
project/namespace dependencies, layer boundaries, circular dependencies, and
infrastructure types exposed by public APIs.

### Findings

A normalized finding includes rule/code ID, severity, message, location,
source engine, and applicable confidence/resolution. Equivalent findings from
multiple engines SHOULD be merged or linked without hiding provenance.
Possible dead code and convention-based relationships remain labeled as
candidates.

`analyze changed` analyzes changed files and the smallest affected project
scope that can provide the requested evidence.

## Validation

```bash
dnaxi validate --profile <fast|standard|full>
```

### Fast profile

The fast profile SHOULD include workspace verification, changed-document
parsing, available affected-project compilation, compiler diagnostics,
configured analyzers for affected scope, non-mutating format verification, and
configured fast structural rules. It MUST NOT modify source.

### Standard profile

The standard profile SHOULD include restore when required, affected builds and
dependents, compiler/analyzer diagnostics, non-mutating format verification,
architecture rules, and affected/configured tests.

### Full profile

The post-MVP full profile SHOULD include full restore,
solution-wide build/tests/analyzers/non-mutating format verification,
architecture checks, configured package/vulnerability policy, public API
checks, and publish checks.

Repository configuration can add, remove, or reorder checks without adding
source-writing checks.

### Validation results and lifecycle

Validation precomputes overall status, passed/failed/skipped/warning counts,
duration per check, top failures, and analyzed scope.

- Passing validation exits `0`.
- Validation or child-operation failure exits `1`.
- Invalid CLI or configuration usage exits `2`.

Child-process codes remain available through `dependency_exit_code`.
`--continue-on-error` collects independent failures.

Raw SDK, test, and analyzer logs MUST NOT flood stdout. The response contains a
concise summary and local artifact path or explicit full-output retrieval
mechanism.

Profiles disclose checks that can execute repository code, write non-source
artifacts, or access the network. Invoking a named profile consents to its
configured checks, subject to the source-write prohibition.

Validation detects VSTest or Microsoft Testing Platform, normalizes both into
one result model, preserves runner exit codes, and applies explicit repository
policy for zero discovered tests. Zero tests MUST NOT silently become success.

Cancellation and timeout exit `1` with `status: cancelled` or `status: failed`
and error code `operation.cancelled` or `operation.timeout`. Completed and
terminated checks remain distinguishable.

## Official `dotnet` operations

The MVP provides:

```bash
dnaxi restore [<target>]
dnaxi build [<target>]
dnaxi test [<target>]
dnaxi format [<target>] --check
dnaxi format [<target>] --apply
```

Later first-class adapters SHOULD cover run, publish, new, project, solution,
package, tool, and workload operations without changing MVP semantics.

### Escape hatch

```bash
dnaxi exec -- dotnet <arguments>
```

The first token after `--` is the selected official `dotnet` executable;
everything after it is pass-through input. `exec` is not an arbitrary shell
runner.

First-class commands reject unknown flags before starting a child process.
Declared pass-through arguments MAY follow `--`, such as test-runner
arguments. Input before `--` cannot bypass validation.

Wrapped commands are noninteractive. Missing required input produces a
structured actionable error.

### Stable MVP flags

| Command | Stable flags |
|---|---|
| `restore` | `--force`, `--locked-mode`, `--no-cache`, repeated `--source` |
| `build` | `--no-restore`, `--runtime`, `--no-incremental`, `--verbosity` |
| `test` | `--no-restore`, `--no-build`, `--filter`, repeated `--logger`, `--results-directory`, runner arguments after `--` |
| `format` | exactly one of `--check` or `--apply`, plus `--no-restore`, repeated `--include`, repeated `--exclude`, `--severity` |

Per-command help and parser contract tests define spelling, arity, defaults,
supported SDK versions, and examples. Conflicts fail with exit `2`.

### Format safety

`format` requires exactly one of `--check` or `--apply`. `--check` uses
non-mutating verification equivalent to `dotnet format
--verify-no-changes`. Only `--apply` may modify source, and its result
identifies modified files. Validation always uses `--check`.

### Structured translation

Results SHOULD include operation, normalized exit, dependency exit, duration,
scope, execution classification, network policy, summary, failure count, and
log artifact. Raw dependency output MAY be retained as an artifact but does
not replace the structured response.

`dotnet-axi` exposes only:

- `0` for successful intent, including empty and no-op results.
- `1` for operation failure, child failure, cancellation, or timeout.
- `2` for CLI/configuration usage errors detected before execution.

The original child code remains in `dependency_exit_code`. Runner-specific
codes, signals, and platform termination codes MUST NOT be confused with
`dotnet-axi` usage errors.

Child SDK processes use a stable CLI language and disable terminal decoration
where supported. Adapters SHOULD prefer structured logs, result files, and
official logger APIs over parsing prose. Localization and SDK message changes
do not alter the stable output schema.

Long-running SDK operations honor cancellation and terminate their complete
child process tree.

### Child safety environment

Every child `dotnet` process receives child-only defaults that:

- Opt out of .NET CLI telemetry.
- Disable background workload advertising-manifest downloads.
- Suppress first-run and logo noise.
- Avoid first-run development-certificate generation where supported.
- Preserve the selected SDK host and required repository environment without
  printing the complete environment.

Explicit workload or update operations MAY perform requested foreground
network work; background update behavior remains disabled.

Results and help distinguish source writes, repository metadata writes,
build/output writes, user-level tool/workload writes, network access, and
repository-code execution. SDK mutations MUST NOT be called read-only merely
because they do not edit C# source.

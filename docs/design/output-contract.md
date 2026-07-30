# CLI and Output Contract

This document defines query planning, command-line behavior, TOON output, exit
codes, schema evolution, and canonical examples.

## Query planning

The planner selects the least expensive engine capable of satisfying the
requested resolution. Ordinary search MUST NOT silently load or compile every
project.

Commands that support partial discovery SHOULD accept `--complete`.
`--explain-plan` SHOULD report the selected engine class, candidate scope,
projects expected to load, and whether full-solution analysis is required.

Contextual suggestions preserve fixed scope flags such as `--solution`,
`--project`, `--configuration`, and `--framework`.

## Evidence envelope

Every normal stdout document includes:

- `schema: dotnet-axi/v1`.
- Canonical `command`.
- `status`: `success`, `partial`, `failed`, or `cancelled`.

Every evidence-bearing document additionally includes:

- `snapshot`: the content-derived workspace snapshot ID.
- `resolution`: `text`, `syntax`, or `semantic`.
- `coverage`: `not-applicable`, `partial`, or `complete`.
- A compact `scope` sufficient to interpret coverage.

Partial semantic or graph results report projects and frameworks considered,
analyzed, remaining when known, excluded or failed, and why coverage is
partial. Complete results name the scope within which completeness is claimed.

Rows include confidence only when it varies per row or is not implied by
response-level resolution.

Collections use deterministic ordering. Unless a command defines a
domain-specific ranking, ties resolve by normalized path, one-based line,
one-based column, kind, fully qualified name, and stable ID. Ordering is not
locale-sensitive.

## TOON encoding

All normal agent-facing stdout, including help and errors, uses canonical TOON
v4.1 and schema `dotnet-axi/v1`. Internal components use typed objects and
convert at the output boundary.

Output is UTF-8 with LF line separators on every platform. Repository and
dependency text follows TOON quoting and escaping rules.

Golden examples and emitted fixtures MUST strict-decode with the pinned TOON
v4.1 corpus. Declared array lengths and tabular row widths exactly match
emitted data. Output-contract blocks do not contain illustrative ellipses.

The .NET serializer implementation is replaceable and is not prescribed by
this design.

## Schema design

Collection rows default to approximately three or four fields. Additional
fields are opt-in through `--fields`.

Large text includes a useful preview, total known size, truncation notice, and
a `--full` or larger-budget escape hatch. Default previews SHOULD generally be
500–1,500 characters.

Commands that can return unbounded collections or source support applicable
controls such as `--limit`, `--fields`, `--max-chars`, `--max-depth`, and
`--full`. Defaults aim to solve common tasks in one call without dumping
repository-scale content.

Results precompute cheap aggregates that prevent predictable follow-ups:
totals, verified/rejected counts, projects considered/loaded, validation
counts, and affected project/test counts.

List and summary responses SHOULD return stable evidence IDs so agents can
request detail without repeating names, paths, signatures, or source.

Schema and truncation changes are evaluated by task success, recovery, and the
ability to judge completeness—not token count alone.

## Empty results

A valid no-match query reports zero results and exits `0`. Empty results retain
the successful schema: counts stay numeric and collections stay zero-length
arrays. A collection does not become prose merely because it is empty.

## Errors and output channels

Errors use the same structured stdout format and include a stable code,
actionable message, and concrete correction.

- **stdout:** structured data, errors, and suggestions.
- **stderr:** debug logs, progress, and dependency diagnostics.
- **exit 0:** success, empty result, or no-op.
- **exit 1:** operation or validation failure.
- **exit 2:** usage or configuration error detected before execution.

Progress never appears on stdout. Unhandled exceptions and dependency stack
traces become a stable internal error with a diagnostic artifact reference.
Debug stack traces MAY appear on stderr only when explicitly enabled.

Diagnostic and raw-log artifact paths are structured fields, not message text.
Artifact contents are outside the stdout TOON document; metadata and retrieval
commands remain structured.

Unknown commands, arguments, flags, requested fields, and conflicting inputs
fail before dependency execution. Usage errors identify invalid input, include
valid flags or concise help, and provide renamed-flag guidance when known.

Every operation is fully expressible through flags and arguments; no operation
prompts interactively.

## Home, help, and suggestions

Running `dotnet-axi` with no arguments shows live workspace state rather than a
general manual. It includes executable path using `~`, a one-sentence
description, workspace path, selected solution/project, cheap project/source
counts, changed-file count, cheap diagnostic status, and a few contextual
suggestions.

Discovery and mutation responses SHOULD include a few relevant complete
commands or templates, preserve fixed scope, use placeholders for runtime
values, and omit suggestions when the response is self-contained.

Every subcommand supports concise `--help` with required arguments,
flags/defaults, and two or three examples.

The CLI supports `--help`, `-v`, and `--version` and SHOULD reserve `update`.
Global `-v` means version only before subcommand dispatch; command verbosity
uses `--verbosity`.

## Schema evolution

Every stdout document begins with `schema: dotnet-axi/v1`.

Backward-incompatible field meanings, collection shapes, or required-field
removals require a new schema major such as `dotnet-axi/v2`. Additive optional
fields MAY ship within v1 only when default row-width and agent benchmark gates
pass.

Unknown requested fields fail with exit `2`; they are not ignored.

## Canonical examples

### Home

```toon
schema: dotnet-axi/v1
command: home
status: success
bin: ~/.dotnet/tools/dotnet-axi
description: "Search, analyze, validate, and safely change the current .NET workspace"
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
suggestions[3]{command}:
  Run `dotnet-axi search symbol '<name>'`
  Run `dotnet-axi analyze changed`
  Run `dotnet-axi validate --profile fast`
```

### Structural search

```toon
schema: dotnet-axi/v1
command: search structural
status: success
snapshot: ws_7c2f5a1d
resolution: syntax
coverage: complete
scope:
  root: ~/src/credit-platform
  paths_scanned: 18400
count: 3
matches[3]{id,file,line,construct}:
  ast_01,src/Orders/OrderRepository.cs,84,invocation
  ast_02,src/Payments/PaymentRepository.cs,112,invocation
  ast_03,tests/DbFixture.cs,39,invocation
suggestions[2]{command}:
  Run `dotnet-axi show document <path>`
  Run `dotnet-axi search structural --pattern '<pattern>' --verify-as invocation`
```

### Verified partial search

```toon
schema: dotnet-axi/v1
command: search structural
status: partial
snapshot: ws_7c2f5a1d
resolution: semantic
coverage: partial
scope:
  projects_considered: 8
  projects_analyzed: 6
  projects_remaining: 2
  partial_reason: Two projects could not restore without network access
discovered: 5
verified: 2
rejected: 2
unresolved: 1
matches[2]{id,kind,name,location}:
  sym_8k2m,method,DbContext.SaveChangesAsync,"src/Orders/OrderRepository.cs:84"
  sym_5p7q,method,DbContext.SaveChangesAsync,"src/Payments/PaymentRepository.cs:112"
suggestions[1]{command}:
  Run `dotnet-axi restore` before repeating with `--complete`
```

### Explicit empty result

```toon
schema: dotnet-axi/v1
command: search symbol
status: success
snapshot: ws_7c2f5a1d
resolution: syntax
coverage: complete
query: LegacyPaymentRule
count: 0
matches[0]:
```

Exit code: `0`.

### Usage error

```toon
schema: dotnet-axi/v1
command: search symbol
status: failed
error:
  code: usage.unknown_flag
  message: Unknown flag `--stat` for `search symbol`
valid_flags[6]{name}:
  "--kind"
  "--project"
  "--path"
  "--include-tests"
  "--include-generated"
  "--help"
```

Exit code: `2`.

### Partial graph

```toon
schema: dotnet-axi/v1
command: search callers
status: partial
snapshot: ws_7c2f5a1d
resolution: semantic
coverage: partial
target: CreditEvaluator.EvaluateAsync
scope:
  considered: 14
  analyzed: 6
  remaining: 8
callers[2]{id,name,location,confidence}:
  sym_2m9c,CreditEndpoint.Handle,"src/Api/CreditEndpoint.cs:31",verified
  sym_4n1x,RenewalWorker.ExecuteAsync,"src/Workers/RenewalWorker.cs:48",verified
suggestions[1]{command}:
  Run `dotnet-axi search callers sym_8k2m --complete`
```

### Validation

```toon
schema: dotnet-axi/v1
command: validate
status: failed
snapshot: ws_7c2f5a1d
profile: standard
execution: executing
network: restore-only
duration_ms: 18439
checks[6]{name,status,errors,warnings}:
  workspace,passed,0,0
  restore,passed,0,0
  build,failed,2,11
  analyzers,failed,1,19
  architecture,passed,0,0
  tests,skipped,0,0
failures[3]{code,message,location}:
  CS8602,Possible null dereference,"src/Rules/RuleEvaluator.cs:73"
  CS1503,Argument type mismatch,"src/Api/Endpoints.cs:28"
  ARCH001,Domain references Infrastructure,src/Domain/Domain.csproj
artifact:
  kind: raw-log
  path: ~/.cache/dotnet-axi/artifacts/run_01J/log.txt
  may_contain_sensitive_data: true
```

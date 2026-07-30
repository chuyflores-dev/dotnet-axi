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

Capabilities provide typed plan candidates. The shared planner discards
candidates that cannot meet the requested resolution or complete-coverage
requirement, then orders the remainder by progressive-analysis level, expected
project loads, planned resolution, engine class, and stable engine identifier.
A plan reports that selection together with planned coverage and the fixed
workspace selectors.

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
locale-sensitive. String-keyed maps order members by their emitted key using
ordinal comparison; typed objects and explicitly declared output fields retain
their declaration order.

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

The CLI currently normalizes typed result payloads to the JSON data model
behind a tool-owned serializer boundary, then encodes that model with its
internal TOON v4.1 writer. Core contracts do not reference the writer.
Conformance pins the official v4.1 encoder corpus, executes every case matching
the fixed comma-delimited, two-space output profile, and accounts for
option-specific cases the serializer does not expose. Representative golden
documents and a deterministic untrusted-string fuzz corpus are produced
through the production boundary and strict-decoded by the pinned reference CLI
in CI.

## Schema design

Collection rows default to approximately three or four fields. Additional
fields are opt-in through `--fields`.

Each command declares its available fields in a canonical order and marks its
compact default set. Requested fields augment those defaults; duplicate names
collapse, and rows retain command-declared order rather than request order.
Field names are ordinal and case-sensitive. An unknown requested field fails
with exit `2`, reports `usage.unknown_field`, and lists the valid fields before
the handler or an executing dependency is created.

For a bounded collection, `count` is the number of rows actually included.
`total_known` states whether `total` and `omitted` are authoritative;
`truncated` is always explicit. Truncated results include a complete
`retrieval_command` using `--full` or a sufficient larger limit. Collection
helpers inspect at most one row beyond the limit when a backend does not
provide a total.

Large text includes a useful preview, total known size, truncation notice, and
a `--full` or larger-budget escape hatch. Default previews SHOULD generally be
500–1,500 characters. Character budgets count Unicode scalar values so a
preview never splits a UTF-16 surrogate pair. Text reports included, total,
and omitted character counts, and requires a complete `retrieval_command`
whenever it is truncated.

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

Successful and partial results exit `0`; failed and cancelled results exit `1`.
The CLI boundary overrides the result-status mapping only for pre-execution
usage or configuration failures, which are structured failed results with
exit `2`. Dependency-specific exit codes remain result data.

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

The CLI command host uses the pinned stable `System.CommandLine` 2.x parser
behind a tool-owned boundary. Parsing produces CLI-owned typed request records;
handler factories and their dependencies are created only after parsing
succeeds. Response-file expansion and bundled short options are disabled so
parsing does not read hidden input or infer undeclared aliases. Invocation
receives explicit stdout and stderr writers and never reads stdin.

## Home, help, and suggestions

Running `dnaxi` with no arguments shows live workspace state rather than a
general manual. It includes executable path using `~`, a one-sentence
description, workspace path, selected solution/project, cheap project/source
counts, changed-file count, cheap diagnostic status, and a few contextual
suggestions.

Discovery and mutation responses SHOULD include a few relevant complete
invocations or templates, preserve fixed scope, use placeholders for values
the caller must supply, and omit suggestions when the response is
self-contained. Suggestions carry an executable and an ordered argument array;
callers invoke that array directly without parsing shell-specific quoting.

Capabilities express suggestions as literal command tokens, observed runtime
values, or named placeholders. The shared composer adds `dnaxi`, appends fixed
workspace selectors in canonical order, removes duplicates, orders by explicit
priority and command, and emits at most three suggestions. It does not infer
runtime values from result content. Capability templates cannot provide
composer-owned workspace selector flags.

Every subcommand supports concise `--help` with required arguments,
flags/defaults, and two or three examples.

Help is a successful structured `help` result. Its `topic` identifies `home`
or the canonical subcommand path; the payload contains usage, description,
operation classification, arguments, flags, registered subcommands, and two
or three complete `dnaxi` examples. Argument and flag arity, required state,
defaults, and inherited state are generated from the active parser
registration. Only registered, non-hidden commands are listed. Help does not
create a command handler or probe workspace capabilities. Help may bypass
missing required inputs for the selected command, but it does not suppress
unknown commands, flags, arguments, or other usage errors.

The CLI supports `--help`, `-v`, and `--version` and SHOULD reserve `update`.
Global `-v` means version only before subcommand dispatch; command verbosity
uses `--verbosity`. Version is a standalone pre-dispatch operation and does
not suppress unrelated input or subcommands.

Version output is a structured `version` result with `tool`, `tool_version`,
and `output_schema` fields. The tool version comes from package-version
metadata embedded in the executable at build time; it is not maintained as a
separate runtime constant.

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
bin: ~/.dotnet/tools/dnaxi
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
suggestions[3]:
  - command: dnaxi
    arguments[3]: search,symbol,<name>
  - command: dnaxi
    arguments[2]: analyze,changed
  - command: dnaxi
    arguments[3]: validate,"--profile",fast
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
suggestions[2]:
  - command: dnaxi
    arguments[3]: show,document,<path>
  - command: dnaxi
    arguments[6]: search,structural,"--pattern",<pattern>,"--verify-as",invocation
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
suggestions[1]:
  - command: dnaxi
    arguments[1]: restore
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
suggestions[1]:
  - command: dnaxi
    arguments[4]: search,callers,sym_8k2m,"--complete"
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

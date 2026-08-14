# Quality Design

This document defines performance targets, correctness testing, security
testing, and agent-task evaluation.

## Performance principles

The tool provides useful commands before full repository semantic analysis
completes. Home, file, text, and syntax search do not wait for a full
semantic index.

Semantic operations attempt to load only candidate projects and required
dependencies. Code graph edges are computed on demand in the MVP.

Within one process, including a composite command, the tool reuses safe loaded
MSBuild state, Roslyn snapshots, syntax trees, confirmed resolutions, and graph
edges. Cross-process warm reuse is not an MVP claim and does not require a
hidden daemon or database.

A later daemon MAY keep workspaces warm, but the CLI remains fully functional
without it, foreground work outranks warming, and every operation verifies
current source state.

Expensive commands support cancellation and SHOULD expose applicable
`--timeout`, `--limit`, `--max-projects`, `--max-depth`, and `--max-chars`
controls.

Commands intentionally performing full analysis disclose scope in stderr
progress and final structured output.

When complete analysis is expensive, a command MAY return a clearly labeled
partial result with explicit verification evidence only when independently
useful and unmistakably incomplete. Mutations and destructive decisions wait
for required completeness.

## Performance benchmark

The repository includes a repeatable large-repository fixture generated from a
committed generator, manifest, and fixed seed. The reference fixture contains
approximately 50,000 C# files across enough projects and dependencies to
exercise catalog, syntax, and candidate semantic work.

The manifest records CPU model/core count, memory, storage, OS, filesystem,
.NET SDK, dependency versions, power mode, fixture hash, and tool commit.
Absolute gates apply only to the designated reference runner; other machines
report comparative results.

For MVP gates, **cold** means:

- A new `dotnet-axi` process.
- Tool-owned cache/state removed.
- Dependencies already restored.
- No daemon.

The harness performs one unmeasured filesystem warm-up, then at least 30
measured cold-process iterations. P95 uses the documented nearest-rank method.

Initial designated-runner targets are:

| Operation | Cold P95 |
|---|---:|
| Home view | ≤ 2 seconds |
| File/text search | ≤ 5 seconds |
| Repository-wide Roslyn syntax search | ≤ 15 seconds |
| Candidate semantic verification of up to 5 projects | ≤ 15 seconds |

The optional Phase 2 warm-session target is a repeated symbol query at ≤ 3
seconds P95 and does not gate the MVP.

Targets change only by editing this design and publishing supporting raw
benchmark evidence and rationale. Comparisons use identical fixture state,
scope, dependency availability, process environment, and output fields.
Different runners or fixtures cannot satisfy an absolute gate.

The home target excludes project-graph evaluation, compilation,
analyzer/generator execution, restore, and network access. Those states MAY be
reported as `not_loaded` or `unknown`.

## Product correctness tests

### Unit tests

Unit tests cover command parsing, unknown flags, exit mapping, TOON
serialization, truncation, empty states, scope selection, query planning,
identity resolution, deterministic ordering, and dependency translation.

### Integration fixtures

Each fixture is defined by a committed `dotnet-axi/fixture/v1` manifest and
explicit template files. The manifest fixes its logical name, random seed,
selected SDK context, destination paths, and whether well-known fixture tokens
are expanded. Workspace files and optional external-import files have separate
destination roots inside the owned fixture directory. Template and destination
paths are repository-relative, validated before materialization, and cannot
escape their manifest or destination root. Every path segment is
NFC-normalized; Windows device names and segments with trailing spaces or
periods are rejected on every host.

Catalog manifests also declare sorted capability identifiers, an optional
VSTest or Microsoft Testing Platform selection, and one build target with an
expected success or intentional-failure outcome. Optional stable output tokens
identify the intended failure or analyzer result without copying
command-specific golden output into the fixture.

Edge-case manifests add a typed scenario declaration. It records the state,
whether failure is intentional, expected coverage as `complete`, `partial`, or
`none`, the coverage classes that remain available, and a stable reason. Git
scenarios additionally declare typed staged, unstaged, untracked, renamed, and
deleted changes or one conflict. Change and conflict content comes only from
validated templates; manifests cannot embed commands.

The fixture factory materializes each instance under a unique owned directory.
The workspace and optional external-import root are separate from isolated
home, Git configuration, NuGet packages and HTTP cache, .NET CLI home, general
cache, temporary, and artifact directories. It returns child-process
environment overrides without mutating the test host environment. Child
processes inherit only an explicit cross-platform allowlist plus fixture-owned
overrides. `dotnet` invocations use one resolved absolute host and an owned
NuGet configuration with explicit package sources, so ambient .NET, MSBuild,
NuGet plugin, and user configuration cannot alter fixture execution.

Factory creation is passive: it does not start Git, `dotnet`, restore,
repository code, or any other process. Tests must opt into tooling, restore, or
repository-code process classifications before the factory creates a
`ProcessStartInfo`. A Git scenario becomes active only when a test explicitly
requests preparation with tooling permission. Preparation creates a
deterministic baseline commit and applies only the typed manifest changes; a
conflict is produced by merging two deterministic fixture branches.

Fixture identity includes a SHA-256 hash over normalized relative paths and
exact materialized bytes. Instance metadata records that hash, the fixed seed,
the optional external-content hash, the scenario contract, the selected SDK
context, and runtime/OS identity without timestamps or machine-specific
workspace paths. The workspace hash describes the passive baseline before an
explicit Git preparation mutates it. Committed fixture inputs are pinned to LF
checkout bytes, and catalog tests pin their expected content hashes.
Owned-directory markers constrain cleanup; root and marker link substitution
is rejected and ownership is revalidated before destructive phases. Transient
cleanup is retried, and a remaining failure preserves the path in a structured
exception so cleanup can be retried.

Catalog verification materializes every manifest and invokes its declared
build target with combined restore and repository-code permission, isolated
process state, and a bounded timeout. The catalog test rejects missing
capability or edge-state classes, unexpected build outcomes, and missing
declared output tokens. Edge-state self-tests separately inspect the declared
filesystem and Git state so build verification cannot mask a missing-assets or
worktree condition.

Fixtures cover:

- Single- and multi-project solutions.
- Multi-targeting and linked files.
- Conditional compilation and generated code.
- Project cycles.
- Analyzers and generators.
- VSTest and Microsoft Testing Platform.
- Uncommitted changes, renames, and deletions.
- Ambiguous solutions.
- Broken projects and missing restore assets.
- Imported files outside the workspace.
- Git conflicts.

### Structural and Roslyn oracles

Syntax-query tests compare product candidates with direct Roslyn syntax-tree
traversal for representative shapes, normalized coordinates, ignore behavior,
malformed input, and no-match behavior.

Semantic tests compare references and relationships with direct Roslyn API
results across overloads, aliases, linked files, multi-targeting,
virtual/interface dispatch, broken projects, and partial/complete scope.

### Mutation safety

Post-MVP tests prove that planning does not write, stale plans are rejected,
apply changes only planned files, validation runs after apply, file fidelity is
preserved, partial write failure is recoverable, and already-satisfied intent
is a no-op where appropriate.

### Output and platform

Golden tests prevent accidental schema bloat and strict-decode every stdout
document with TOON v4.1.

Cross-platform tests cover paths, LF-only TOON, executable discovery, hook
merge/removal, supported file permissions, and child-process-tree cancellation
on the published platform matrix.

TOON conformance tests provenance-pin the official encoder corpus, run every
case applicable to the fixed output profile, inventory option-only cases, fuzz
untrusted strings and control characters through the production serializer,
and verify UTF-8, LF-only output, declared array lengths, and row widths. CI
strict-decodes golden and generated fuzz documents with the pinned reference
CLI.

SDK adapter tests run under at least one non-English host locale and cover both
VSTest and Microsoft Testing Platform translation.

### Security

Integration tests prove passive commands do not initiate network access,
restore, workload advertising downloads, or .NET CLI telemetry. Executing
tests verify child safety environment and declared network classification.

Security tests cover shell metacharacters, malicious paths, symlink
substitution, output injection, secret redaction, artifact permissions and
retention, and the passive/executing boundary.

Entity-ID tests resolve unchanged IDs across fresh processes and after cache
deletion and prove stale IDs never bind to another overload or declaration.

## Agent-task benchmark

Agent evaluation is split by purpose so release feedback stays small and a
provider adapter does not become a second product.

### Objective and scoring

The primary outcome is a working repository change, not an answer or a
particular command sequence. Evaluation is lexicographic:

1. Deterministic build or test validation passes.
2. The diff stays inside the task's allowed production scope.
3. Total tokens per verified task.
4. Wall-clock seconds per verified task.
5. Tool calls and retries as diagnostic measures.

Correctness and scope safety always outrank efficiency. Final prose and command
sequence are retained for diagnosis but never graded as the task outcome.

### Evaluation lanes

The active evaluation has three independent lanes:

1. **Deterministic CLI corpus.** Pull-request tests invoke commands directly
   and verify semantic facts, structured diagnostics, evidence fields, TOON
   conformance, bounded output, and applicable latency. No model runs.
2. **Candidate agent canary.** A manually dispatched release check runs one
   candidate execution for every applicable repository-change task. It answers
   whether the agent produced a scoped change that passes deterministic
   validation using the candidate skill and package. It has no raw-tool
   condition or repetition matrix.
3. **Paired claim comparison.** A separate manually dispatched series exposes
   equivalent task state and ordinary tools to baseline and candidate runs. It
   runs only for a milestone or named agent-experience claim.

A result from one lane does not reclassify another. This keeps command
correctness, skill activation, complete-task behavior, and comparative
efficiency visible without proving all four through provider-specific command
reconstruction.

### Manual benchmark script

`eng/benchmark-agent.ps1` is the active real-agent harness. An operator invokes
one corpus task and condition at a time. The script materializes a fresh
fixture, exposes the candidate skill and source-pinned package only for a
candidate run, invokes Codex once, grades the result, appends one JSONL record,
and deletes the workspace. It has no preparation manifest, scheduler, provider
adapter, retry, or reconciliation phase.

Before the timed agent run, the script creates a uniquely versioned package
from the current worktree when no feed is supplied, then verifies and warms
the source-pinned package. An explicit feed remains available for reproducing
a retained candidate. Package build and acquisition are setup rather than
agent-task work, matching the preinstalled interfaces used by comparative
benchmarks.

The script pins the exact model, reasoning setting, task timeout, product
version, corpus, and package feed supplied by the operator. Web search is
disabled. Its normal mode retains Codex's workspace sandbox. `-OuterIsolated`
disables the inner sandbox and is valid only when the operator already placed
the script inside a disposable VM or container containing no unrelated source
or credentials. Credentials are never written to evidence.

The default release canary uses `gpt-5.6-luna` at its lowest supported
reasoning effort, `low`. Operators may override both values for an explicitly
named comparison, and the result records the effective pair.

The release canary is the set of individually retained candidate runs for the
applicable release-critical repository tasks. Each task runs once. The initial
corpus contains one ambiguous-owner refactor and one multi-targeted feature
addition. Both require Roslyn/MSBuild-backed declaration ownership before the
edit. A diagnostic rerun is a new result and never replaces or hides the
original run.

The agent may edit the repository and return any final prose. After it exits,
the harness captures the complete Git diff, rejects paths outside the task's
allowlist, materializes a validator that was absent during the agent turn, and
runs its package-free build or executable checks. The agent cannot pass by
formatting an answer or editing the validator.

The script emits `dotnet-axi/agent-benchmark-run/v2`. Each run records:

- Task ID and process completion or timeout.
- Changed and unexpected paths plus deterministic validation outcome.
- Input, cached input, cache-write input, fresh input, output, reasoning
  output, and total tokens.
- Wall-clock duration, turns, and tool-call count.
- Whether `dnaxi` was invoked and whether an observed invocation exited
  nonzero.
- Condition, exact model, reasoning setting, and product version.

The gate uses only process completion, an allowed nonempty diff, and
deterministic validation. It does not require tool activation or treat a
recovered command diagnostic as task failure. A process failure before any
model turn is a `harness` failure rather than an agent or product failure.
Provider JSONL, stderr, validation output, and the final response are retained
as diagnostic artifacts. Final prose is never parsed as product evidence.

A release candidate passes when every applicable candidate task changes only
its allowed production paths, passes its independent validator, and neither
times out nor fails to start. Activation and nonzero `dnaxi` exits are recorded
rather than independently gated: a recovered diagnostic is not a failed task,
while an unrecovered tool problem prevents the repository outcome from
passing.

List the available tasks and run one candidate task manually:

```powershell
./eng/benchmark-agent.ps1 -ListTasks
./eng/benchmark-agent.ps1 `
  -Task add-ledger-try-format
```

### Paired improvement claims

The raw-tool baseline uses ordinary real file readers, `rg`, and `dotnet`.
Baseline and candidate use fresh equivalent fixtures, the same agent, exact
model, reasoning setting, prompt, permissions, and outer isolation. Only the
candidate receives the matching skill and `dnaxi` access.

The same script accepts `-Condition baseline`, exposes neither the skill nor
the package, and shadows `dnx`, `dnaxi`, and `dotnet-dnaxi` in the agent's
command path. One matched baseline/candidate execution per task is the default.
Prefer more representative tasks over repeated executions of a small synthetic
set. Repeat the complete paired suite only when a formal claim needs additional
confidence; never rerun or discard only unfavorable cases.

A named improvement claim requires equal or higher verified success, no
safety-critical regression, and at least 10% lower median total tokens and
duration across matched verified tasks. Claims remain scoped to the exact
agent, model, corpus, and harness. Different agents or models are never pooled.

### Evaluation cadence

Real-agent canaries and paired comparisons are explicit manual runs and never
run in pull-request CI. Pull requests run deterministic command and harness
self-tests only. A release runs the applicable candidate canary. Major
milestones and public comparative claims additionally run the paired suite.
New real-world failures become focused corpus cases; the benchmark does not
grow provider-specific reconciliation machinery to explain them.

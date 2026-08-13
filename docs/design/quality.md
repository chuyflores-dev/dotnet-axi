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

The repository includes a repeatable harness where supported coding agents
complete representative .NET tasks against controlled repositories.

### Rollout and agent adapters

The task corpus and result schema are agent-neutral. Each agent has a thin
adapter for noninteractive execution, event and usage capture, permissions,
timeouts, and exact model selection. Baseline and candidate results are
compared only within the same agent, exact model, reasoning setting, and
harness version; results from different agents or models are never pooled into
one product-effect number.

Benchmarking rolls out with usable product capability:

| Version | Benchmark stage |
|---|---|
| `0.2.0` | Ship the installable skill and perform only development smoke runs; make no measured agent-experience claim |
| `0.3.0` | Build the agent-neutral corpus and runner, add the Codex adapter, and run the first measured discovery-task comparison |
| `0.4.0` | Prove that Codex actually selects the exact version-pinned `dnx` path on the existing discovery corpus before adding more product commands |
| `0.5.0`–`0.8.0` | Add applicable semantic, impact, analysis, execution, and validation tasks and manually run the affected Codex subset before each release |
| `0.9.0` | Add the Claude adapter and run the same conditions as an initially advisory second-agent series |
| `0.10.0` | Produce full Codex and Claude release evidence; keep every claim scoped to its agent, exact model, and harness |

Real-agent benchmarks are explicit, manually dispatched runs and never execute
on every pull request. CI uses deterministic fake-agent self-tests for the
harness. Credentials are supplied only to the selected adapter process and are
never included in prompts, fixtures, trajectories, or published artifacts.
Adapter readiness, corpus readiness, and a measured series are separate gates;
a successful adapter smoke run does not satisfy a milestone's measured
comparison.

The initial Codex adapter uses supported noninteractive JSONL output and
records the CLI version, exact model, reasoning setting, sandbox, instructions,
event stream, and reported usage. Runs are ephemeral and isolate user-level
configuration from the controlled benchmark condition.

The adapter launches one absolute, version-pinned Codex executable as `codex
exec --ephemeral --json --ignore-user-config --ignore-rules
--skip-git-repo-check`. Controlled benchmark fixtures are content-hashed clean
directories rather than Git repositories, so the explicit skip affects only
Codex's repository-presence preflight. Every argument is passed without a
shell. Each run also passes the exact model and workspace and selects a sealed
permission profile whose root rule is deny. The profile reopens only minimal
system resources, the run's workspace with `read` or `write` access, its
materialized condition artifacts with read access, and its isolated runtime
state with write access; network access, the shared authentication home, and
host temporary roots remain denied. A task receives workspace write access
only when its abstract permitted tools declare `workspace-write`; every other
task is passive. The invocation also fixes the reasoning and `never` approval
settings and disables web search.
Condition-specific configuration accepts only declared skill and MCP-server
exposure, whose instruction and concrete-tool hashes are pinned in the series
manifest. Authentication environment is supplied explicitly to this one
launcher process, is not included in captured arguments or evidence, and is
explicitly denied to commands executed inside the agent sandbox.

The first successfully created launcher process owns the run even before a
`thread.started` event arrives. Its PID is retained once, silence while that
process remains live is not a start failure, and no adapter-internal retry is
performed. The runner's task timeout is the total deadline. Timeout first
snapshots available normalized and raw evidence, then the existing bounded
stop/dispose lifecycle terminates, waits for, and reaps the exact process tree.

Codex stdout is framed as JSONL and retained verbatim with contiguous sequence
numbers and hashes. A nonempty final fragment without a newline is truncated,
even when it is otherwise valid JSON. The adapter also retains stderr plus
process-start and process-exit evidence, including PID and exit code. It
normalizes the immutable final agent message, turn usage, command executions,
file changes, tool outcomes, and portable inspected file/project scope.
One thread and one turn follow an explicit start, item, and terminal transition
model; duplicate or out-of-order lifecycle events fail closed. Bare, rooted,
and quoted repository paths are normalized only after preserving root
semantics, and recognized paths outside the workspace are unsafe. Bounded
shell wrappers are decoded before command-scope extraction, and command-class
selection uses the invoked executable rather than executable names appearing
only as arguments. Malformed,
duplicate, overflowing, permission-denied, read-only, network-denied, and
untrusted-scope evidence fails closed while preserving the complete trajectory.
For every repository-read or source-search command, including path-qualified
readers and shell input redirection, the adapter records a synthetic
`adapter.filesystem.read.denied` event for each absolute, traversal,
shared-state, or symlink-resolved operand outside the run's readable roots.
The event retains the resolved path observed while the run fixture is still
live. Reader or shell grammar that cannot be reconciled completely emits
`adapter.filesystem.read.unreconciled` and fails closed. Reconciliation binds
each event to the matching provider command and requires every independently
derivable attempt, regardless of provider item status or command exit code.
After timeout, the runner preserves the pre-cleanup snapshot, performs bounded
stop and dispose, and then retains only a monotonic extension containing the
owned launcher's final PID and exit-code evidence.

The adapter smoke is manually dispatched and separate from the S15 measured
series. Copy and fill
`tests/Fixtures/CodexAdapter/manual-smoke.example.json`, then run:

```bash
dotnet run --project tests/DotNetAxi.CodexSmoke/DotNetAxi.CodexSmoke.csproj \
  --configuration Release -- \
  --manifest /absolute/path/to/smoke-request.json \
  --output /absolute/path/to/new-smoke-evidence.json
```

The request pins an absolute Codex executable, declared CLI version, isolated
authenticated `CODEX_HOME`, controlled workspace, exact model and reasoning,
settings and condition hashes, sandbox, approval, network, timeout, and cleanup
policy. The create-new evidence file retains normalized and raw results and
mechanically reconciles raw sequence/hashes, turn usage, turns, tool calls,
final answer, PID, and exit code. It hashes but does not publish the
authentication-home path and never records credential contents. Canonical
builds compile the smoke project but never execute it; no PR CI path dispatches
Codex or supplies credentials. Successful smoke evidence is readiness evidence
only.

The later Claude adapter uses supported noninteractive streaming JSON and
records the equivalent model, permission, turn-limit, event, usage, cost, and
version evidence. Adding Claude does not redefine or invalidate the Codex
series; it tests whether the product benefit generalizes to another agent
harness.

Adapter contract tests include clean success, explicit permission denial, read-only repository, external worktree, disabled network, silent-but-live reasoning, stalled worker, and malformed event-stream scenarios.
Oracles distinguish a product failure from a host restriction and require bounded cleanup without duplicate workers or repeated permission retries.
Codex runs use explicit ephemeral JSONL and sandbox settings; Claude runs use the equivalent documented permission and streaming controls.
The full cross-platform restricted-host matrix remains deterministic and does not require paid agent execution in pull-request CI.

Every run captures:

- Task success and correctness.
- Unsupported or unjustified claims.
- Input and output tokens.
- Agent turns and tool invocations.
- Wall-clock duration.
- Files and projects inspected.
- Required validation execution and result.
- Agent CLI/version and exact model ID.
- Reasoning setting and prompt/instruction hashes.
- Tool permissions and network policy.
- Task-fixture commit and `dotnet-axi` commit/schema.
- Randomization order, timeout, and raw trajectory result.

Candidate and baseline conditions use the same agent, model, settings, and task
state. Each task/condition runs at least five times in interleaved randomized
order.

The runner emits the agent-neutral
`dotnet-axi/agent-benchmark-result/v1` contract. One immutable series manifest
pins the corpus, harness, adapter, agent version, exact model and reasoning
setting, settings hash, fixture commit, product commit and schema, dispatch
mode, run count, randomization seed, and immutable baseline and candidate
condition descriptors. A single execution-settings value supplies both
conditions, so model, settings, permission profile, and disabled-network policy
cannot drift between them. Condition descriptors identify only their
condition-specific instruction and concrete-tool configuration hashes; the
runner rejects swapped descriptors or adapter-observed settings and hashes that
differ from the controlled input. It snapshots and revalidates public corpus
records before scheduling, fixture creation, or adapter work; malformed task
identities, collections, policies, applicability, oracles, and validation
contracts fail closed.

The seed drives a stable shuffle of the complete
task-by-condition-by-repetition matrix, rather than adjacent condition pairs.
Execution order is assigned after the shuffle and retained in every result, so
the schedule can be replayed without carrying a condition-order bias. Every
applicable task/condition has at least five independently materialized fixture
runs. The shared fixture factory supplies a clean task state. A bounded,
domain-separated before/after fingerprint covers the root and complete
workspace file and directory inventory with explicit regular, directory,
special, and reparse-point types. It never follows a root or entry reparse
point, reads content only for declared regular fixture files after their
inventory metadata still matches, and never reads arbitrary added-file
content. Missing content, entry limits, inspection timeouts, and I/O failures
produce an incomplete unsafe result while preserving normalized and raw run
evidence.

An adapter start failure is retryable only when it certifies that no live agent
execution was created. After an execution starts, the runner never starts a
replacement for that logical run. Before any permitted pre-start retry, the
runner proves that declared content and the full inventory still match the
initial state. Completion and timeout first snapshot normalized evidence, then
every completed, timed-out, or cancelled execution receives exactly one
bounded stop and dispose before final integrity is measured. Timeout results
retain the last normalized progress and raw events rather than discarding
partial tokens, turns, calls, or inspected scope.

Normalized results retain the immutable final answer; success, exact-fact
support, fixture integrity, safety, and required-validation outcomes are
derived by the runner from corpus oracles wherever deterministic. Results also
record input/output/total tokens, turns, ordered tool calls, wall duration,
ordinal inspected file and project sets, timeout and start-attempt state,
versions, all controlling hashes, permission/network policy, and separate
immutable `claims-supported`, `network-unused`, and `workspace-unchanged`
outcomes. Inspected scope uses the fixture system's canonical portable
relative-path rules. Source-search command text is not reinterpreted as a
general shell or search-tool grammar: relative inspected scope comes from
reported result paths, while explicit rooted or traversal source references
still receive a containment check. Repository-read operands and all reported
source paths remain scope evidence. This keeps glob and regular-expression
query tokens out of inspected scope while real outside-workspace evidence
fails closed. Exact-fact answers are canonicalized as unique, ordinal-sorted
sets before comparison, so natural response ordering does not change
correctness. Provider events remain opaque immutable strings with
contiguous sequence numbers and per-event SHA-256 hashes; the result pins the
ordered raw trajectory with a separate SHA-256 hash. Negative or overflowing
token metrics, invalid scope, unpermitted tool classes, missing raw evidence,
hash drift, malformed ordering, and unknown normalized statuses fail closed.

Dispatch is explicit. Continuous-integration dispatch rejects every adapter
except the exact sealed in-process deterministic fake type; adapter metadata
cannot self-attest fake status. The descriptor is read and snapshotted once.
Real-agent adapters require manual dispatch. The fake implements the same
public adapter and lifecycle contracts and deterministically supplies answers,
usage, inspected scope, tool calls, observed configuration, and raw events from
the controlled input. It neither launches an agent nor exercises provider
credentials.

The minimum baseline uses ordinary file reads, `rg` or equivalent search, and
raw `dotnet` commands. It receives no hidden `dotnet-axi` guidance or
artifacts. Additional comparisons MAY include Roslyn-oriented MCP servers or
other code-intelligence tools. The primary candidate installs the released
`dotnet-axi` skill and gives the agent access to the matching `dnaxi` package.
An optional direct-CLI/no-skill condition MAY isolate the skill's contribution.

Deterministic repository, compiler, and test oracles decide success whenever
possible. A model judge is used only for qualities that cannot be reduced to a
deterministic oracle; it receives blinded condition output, uses one pinned
judge configuration across compared conditions, and is recorded in the run
manifest.

### Corpus contract

The agent-neutral corpus uses
`dotnet-axi/agent-task-corpus/v1`. Its corpus ID and semantic version are
independent of the product and harness versions. Each task declares:

- A stable task ID, first applicable product milestone, and required product
  capabilities.
- One condition-neutral prompt and explicit baseline and candidate
  applicability. Prompts and judge rubrics cannot name either condition,
  `dnaxi`, or `dotnet-axi`; adapters expose condition-specific tools without
  changing the task text.
- A fixture manifest, fixture name and seed, materialized workspace content
  hash, and a clean materialized state. Fixture manifests are resolved inside
  the corpus directory and validated through the shared fixture factory.
- Abstract permitted tool classes, a bounded timeout, disabled network, the
  invariant locale, and UTC. The adapter maps permitted classes to concrete
  tools for its condition without adding tool-selection hints to the prompt.
- A success oracle, a separate safety oracle, and required harness validation.
  Legacy discovery tasks use an exact set of newline facts normalized by
  `ordinal-lines/v1`. The 0.5.0 symbol-context corpus uses the ordered,
  duplicate-preserving `ordinal-sequence/v1` normalizer; its condition-neutral
  prompt names every required line prefix in oracle order without revealing
  expected values. Their deterministic safety checks require supported claims,
  no network use, and an unchanged workspace. Validation always confirms the
  fixture hash and executes both oracles.

Corpus consumers select only tasks whose first milestone is not later than the
measured product and whose required capabilities are present. Adding tasks for
a later milestone therefore does not turn an earlier release's intentionally
unshipped capabilities into missing evidence.

The controlled `0.3.0` set contains file-name, literal-text, regular-expression,
invocation, attributed-class, object-creation, and catch-clause discovery. One
deterministic C# fixture includes qualified forms, generated noise, textual
false candidates, and syntax that must remain unresolved without semantic
inference. The task prompt describes the outcome and response facts, not a
condition-specific command.

The controlled `0.5.0` symbol-context set adds declaration discovery, explicit
solution, project, and test scope, owner and framework variants, fresh identity
resolution, stale and ambiguous correction, semantic candidate verification,
bounded symbol and exact document-span retrieval, outline, and whole-section
context truncation. A fixed multi-solution fixture and exact fact oracles keep
the prompts condition-neutral while deterministic pull-request tests prove the
success, stale, ambiguous, partial-coverage, truncated, and unsupported
outcomes without dispatching a paid agent. Relationship and mutation
capabilities remain invalid for pre-`0.6.0` corpus tasks. All ten tasks apply to
the candidate. Four tasks also apply to the raw-tool baseline: explicit
test-symbol scope, owner/framework variants, partial semantic verification,
and exact document-line retrieval. The other six tasks are candidate-only
because their required identity, bounded-show, outline, or composed-context
behavior does not have an equivalent raw-tool condition.

Codex discovery series are prepared and dispatched only through the manual
`DotNetAxi.CodexBenchmark` console. A strict request pins the candidate package
and skill, instruction and tool artifacts, authenticated `CODEX_HOME` path
identity, executables, settings, commits, and the exact selected corpus.
`prepare` recomputes every artifact hash, hashes every executable-search
directory, verifies the exact CLI version and active ChatGPT authentication
with bounded local `codex --version` and `codex login status` probes, and seals
the deterministic 70-run schedule without dispatching a model. It also
executes the pinned bounded reader, raw `dotnet`, and an `rg`-compatible
source-search command so a sealed but unusable baseline fails before paid
execution. The source-search probe covers both exact fixed-string lookup and
the `--files`, `--hidden`, `--glob`, and `-g` grammar commonly emitted by Codex;
a grep-backed executable that implements only the first lookup is not treated
as equivalent.
Preparation executes the exact source-pinned candidate with `-- --version` against
disposable isolated .NET and NuGet state and requires the expected successful
structured schema and tool version. `run` repeats that probe while validating
the unchanged preparation before it creates evidence or dispatches a paid
agent. It then writes create-new preparation, per-run, report, and summary
evidence; completed run evidence is flushed durably before the next run starts,
but only after the owned agent process has exited and been reaped. The evidence
root is never part of the agent-readable filesystem profile. Every execution
artifact and tool-directory pin is revalidated after each retained run before
another paid run may start.
`validate` reloads the request and artifacts, rejects unknown JSON fields or
drift, reconciles normalized metrics with raw Codex events, and recomputes the
documented comparison thresholds. Missing, failed, and timed-out trajectories
remain explicit and make the comparison incomparable rather than contributing
smoothed success or efficiency values.

The `0.4.0` self-hosting request uses request and preparation version 3. It
pins an executable `dnx`, a local feed containing only the exact
`dnaxi.0.4.0.nupkg`, and the independently hashed repository Agent Skill. The
package preflight verifies the `dnaxi` ID, 0.4.0 version, .NET tool type,
command settings, required tool payload, and absence of Agent Skill entries;
the feed filename alone is not accepted as candidate identity.
Baseline and candidate use the same
isolated raw-tool `PATH`, with the pinned `dnx` directory first, no other `dnx`
in later entries, and no persistent `dnaxi`. Baseline disables skills.
Candidate adds only the repository skill through the project-local
`.agents/skills/dotnet-axi` discovery path and a `DNAXI_LOCAL_FEED` environment
value bound to the pinned feed. A network-free `codex debug prompt-input`
preflight proves the candidate skill and exact source-pinned 0.4.0 invocation
are model-visible while the baseline does not expose the skill before paid
execution is allowed. Each materialized workspace supplies an isolated
runtime-state sibling for .NET, NuGet, temporary files, and diagnostic
artifacts. The adapter copies only the condition-permitted executable
directories into a run-specific artifact root and copies the pinned feed only
for candidate runs. The candidate skill is copied only into that candidate's
workspace. Neither condition receives the sealed source paths, the other
condition's artifact or fixture instance, or another run's state. The
deny-root profile grants access only to that workspace, artifact root, and
runtime state; direct `/tmp`, including `/tmp/.dotnet`, and the platform host
temp root remain denied. Node reuse is disabled and the same profile runs the
local execution preflight, including an MSBuild property evaluation. The skill
can therefore select
`dnx dnaxi@0.4.0 --source "$DNAXI_LOCAL_FEED" --verbosity quiet --` while
package resolution remains local and network-disabled. The CLI is not
represented as an MCP server, and API-key authentication or API-key artifacts
are not accepted.

The corrected `0.4.0` Codex discovery protocol uses version 2 retained-run and
report schemas, version 3 summaries, Codex adapter version 1.7, and harness
version 2.3. Reconciliation corrections do not relabel retained adapter
evidence. The initial `0.5.0` symbol-context protocol uses request and
preparation version 4, retained-run and report version 3, and summary version
4. The first
retained series used harness 2.4 and remains failed/incomparable. The initial
repaired release-gate series used harness 2.5 and corpus 1.0.1; it also remains
failed/incomparable because the prompt contract left fact value grammar and
exact whitespace under-specified while a grep-backed `rg` wrapper rejected
common Codex arguments. The subsequent retained release-gate series used
harness 2.6 and corpus 1.0.2. Every prompt defines the condition-neutral value
grammar,
literal prefix, required single space, and ordering without disclosing the
evidence values to discover. The `ordinal-sequence/v1` normalizer preserves
line order and duplicates, so reordered or repeated facts fail the exact
oracle without reinterpreting retained series that used `ordinal-lines/v1`.
That 2.6 / 1.0.2 evidence remains immutable, failed, and incomparable: its
path-scoped partial syntax routes did not expose the requested selector in
structured output, and expected ambiguity was not fully reconciled as a
structured diagnostic outcome. The route-corrected protocol uses harness 2.7
and summary schema version 5. The isolation-corrected protocol uses request and
preparation version 5, Codex adapter version 1.8, harness 2.8, isolation
protocol `codex-permission-profile/v1`, the same summary version 5, and the
unchanged corpus 1.0.2. The condition-neutral semantic-verification repair uses
harness 2.9 and corpus 1.0.3 for future evidence. Its shared task asks only for
the candidate location, evaluated C# project ownership, and whether an owning
project makes compiler-backed verification available. The prompt maps observed
ownership to `present` or `absent` and verification availability to `available`
or `unavailable`; it does not ask either condition to reproduce product field
names or hidden outcome vocabulary. This repair does not rewrite, relabel, or
pool any retained series. It schedules five
repetitions for each applicable condition: four shared tasks produce 20
baseline and 20 candidate runs, and six candidate-only tasks produce 30 more
candidate runs, for 70 randomized runs and a 9,000-second agent-timeout budget.

Preparation runs the non-model isolation preflight once for baseline and once
for candidate before any paid dispatch. Each probe must execute its
materialized reader, raw source search, and .NET SDK and read its own fixture
and condition artifacts, while unique sentinels representing immutable
request/preparation, retained evidence, candidate-only artifacts, another run,
the other condition's fixture and artifacts, shared Codex state, and host temp
state remain unreadable through absolute paths, parent traversal, and a
workspace symlink. An unsupported permission profile, unavailable symlink
probe, readable sentinel, missing permitted read, nonzero exit, unexpected
output, or stderr causes preparation to fail closed.

The shared sealed raw-tool path contains the pinned `dnx`, an executable `sed`
reader, raw `dotnet`, and `rg`-compatible source search. Preparation runs
the bounded `sed -n 1,110p` form against the complete candidate `SKILL.md`,
finds an exact source line in a materialized pinned fixture, proves the common
Codex `rg` argument grammar against that fixture, and evaluates its target
frameworks through raw `dotnet msbuild`. It also locates the shared semantic
candidate and evaluates the `Compile` items of every fixture C# project through
ordinary `dotnet msbuild -getItem:Compile` commands, proving that raw baseline
tools can establish absent project ownership and therefore unavailable
compiler-backed verification. It fails before paid execution when
any required command is absent, cannot run, rejects that grammar, or produces
different evidence. The shared semantic-verification task explicitly permits
the adapter's `dotnet-sdk` tool class, so a baseline raw `dotnet` invocation
that passes preflight remains policy-conforming in retained evidence. The
network-free local Codex probes use a 30-second deadline so runtime cold starts
under parallel CI remain bounded without inheriting the paid-run budget. The
fixture home restores this sealed path after login-shell initialization, and
every run revalidates that the login shell resolves `dnx` to the pinned
executable before Codex starts. The protocol derives normalized and raw-event
command classification and inspected scope through the same rules.
Condition metrics count command executions of the manifest-pinned `dnx
<package-id>@<version>` identity only when the command also carries the pinned
local source and quiet verbosity before the tool delimiter. Successful
invocations, activated runs, and successfully activated runs are recorded in
aggregate and for every discovery task. Mentioning that vector as data inside
another command, omitting source isolation, or invoking a different package
version is not activation. Reconciliation decodes the bounded POSIX shell
display forms emitted by Codex so quoted search expressions do not hide an
otherwise exact invocation. Capability identities map to the public command
grammar; attributed-class activation is `search syntax class` with exactly one
nonblank `--attribute` value. A complete comparison with zero candidate
invocations is labeled `zero-activation`; a discovery task with no successful
activated candidate run is labeled `activation-gap`. Either blocks the release
and cannot support an improvement claim.

For the `0.5.0` series, activation is a task-specific ordered vector rather
than merely the last command. Fresh and bounded symbol-show tasks retain
`search symbol -> show symbol`; symbol outline retains `search symbol ->
outline`; symbol context retains `search symbol -> context symbol`; stale and
ambiguous fixture identities may begin with `show symbol`. The explicit
multi-solution test-symbol task retains its `search symbol` selector with
`--solution Workspace.slnx --include-tests`. Exact vector observation is
reported independently from command success. Normal success-returning tasks
require successful commands before they can become successful activations.
The stale and ambiguous tasks instead require their exact fixture identity,
the public diagnostic exit code `1`, and a completed command item. Schema,
command, failed status, error code, the exact `dnaxi search symbol` correction
for the expected unqualified name and `src/Core/Core.csproj` scope, the declared
field set and `--full`, candidate count and identity data, requested identity,
and scope must all reconcile with the task's expected facts; only then can a
successful task run count as a successful activation even though its
command-success vector is false. An arbitrary nonzero exit, malformed output,
unexpected or wrong error code, missing correction data, scope drift, or
identity drift remains an unsuccessful activation.
Candidate identity reconciliation requires canonical public `symbol/v2` IDs,
positive line locations, and the exact controlled fixture files. Ambiguous
candidates must have distinct IDs and cover both relocated declarations;
duplicated identities do not reconcile even when names, signatures, and counts
match. The story-scoped structured reader rejects root fields not owned by the
declared command and rejects unknown or misnested fields and containers at
every parsed depth while accepting the live route shapes used by the controlled
corpus.
Every retained step records its order, route, selector, test and generated
eligibility, and the command's `scope.considered` value derived from raw output.
Reconciliation compares every explicit requested selector with its structured
output selector and requires every applicable eligibility field to explain the
effective scope; missing evidence does not reconcile. Path-scoped syntax output
therefore retains its normalized repository-relative `scope.paths` selector in
both complete and partial semantic results. Top-level payload paths are not
reinterpreted as scope selectors. The controlled counts
are six eligible C# files for `Workspace.slnx --include-tests`, four for
`src/Core/Core.csproj`, and one for `loose/UnownedCandidate.cs`; generated
eligibility is false in the controlled fixture. Successful activation means a
successful task run with the exact reconciled vector; command success remains a
separate per-step fact because stale and ambiguous identity tasks intentionally
exercise structured nonzero command outcomes. Summary generation from
normalized adapter results and independent raw-event replay both call the same
structured-output and route reconciler, so neither path can accept a weaker
diagnostic, scope, or identity trajectory.

Activation reconciliation accepts one or more valid leading POSIX environment
assignments before the invoked `dnx` executable, including quoted or escaped
values. A leading `PATH` assignment remains invalid because it can substitute
an unpinned executable. Quoted assignment names, malformed names, unquoted
Unicode whitespace that POSIX shells retain within a word, and POSIX
assignment syntax inside a PowerShell wrapper do not move the executable
boundary.

The version 3 `0.4.0` summary remains immutable historical evidence. The
`0.5.0` request pins its artifact and the known request and report identities,
strictly validates the complete 70-run summary shape, and requires its recorded
`complete` / `no-improvement` conclusion without reclassification. Its runs and
metrics are never pooled with the symbol-context series. Within `0.5.0`, regression and
improvement thresholds use only the four shared tasks. Completion, safety,
success, and activation for each of the six candidate-only tasks are reported
separately and cannot improve or dilute the comparable cohort.

Exact fact sets must be nonempty, unique, and stored in ordinal order. Strict
corpus loading rejects unknown fields, duplicate or unsorted outcomes, fixture
identity or content-hash drift, omitted validation, moving or mutable setup,
ambient network/locale/time-zone settings, and condition-specific guidance.
If a later task genuinely needs a model judge, its oracle instead declares an
independently versioned rubric, requires condition blinding, and adds explicit
model-judge validation; deterministic and model-judged criteria cannot be
mixed in one oracle.

Scenarios SHOULD cover declaration lookup, exact references, conceptual
discovery, caller/impact analysis, diagnostic investigation, targeted change,
architecture detection, changed-scope validation, and multi-project failure
diagnosis. Mutation tasks enter the gate only when the release contains those
features.

Affected-test selection MAY begin as a transparent heuristic. It reports its
evidence and confidence, and its exact algorithm may evolve behind the stable
result contract.

Changes to schemas, defaults, suggestions, context construction, or composition
SHOULD fail the applicable gate when evidence shows:

- Any safety-critical regression.
- Aggregate success decreases by at least 2 percentage points.
- Median token or tool-call use increases by at least 10% without documented
  correctness benefit.

To claim an agent-experience improvement for a named agent/model/harness, a
release demonstrates both:

- Equal or higher aggregate success than the raw-tool baseline with no
  safety-critical regression.
- At least 10% lower median total token consumption across complete task
  trajectories.

A safety-critical regression occurs when a successful-baseline case becomes an
unsafe or unjustified success. Supporting evidence publishes turns, tool
calls, duration, validation completion, per-task outcomes, and raw run data.
Per-task token increases are acceptable only with measured and documented
correctness, completeness, or safety benefit.

Claims remain scoped to the tested agent, model, and harness.

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
shell. Each run also passes the exact model, workspace, and `read-only` or
`workspace-write` sandbox explicitly; fixes the reasoning and `never` approval
settings; and disables web search and workspace-sandbox network access. A task
receives `workspace-write` only when its abstract permitted tools declare
`workspace-write`; every other task is passive and must use `read-only`.
Condition-specific configuration accepts only declared skill and MCP-server
exposure, whose instruction and concrete-tool hashes are pinned in the series
manifest. Authentication environment is supplied explicitly to this one
process and is not included in captured arguments or evidence.

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
semantics, and recognized paths outside the workspace are unsafe. Malformed,
duplicate, overflowing, permission-denied, read-only, network-denied, and
untrusted-scope evidence fails closed while preserving the complete trajectory.
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
  Discovery tasks use an exact set of newline facts normalized by
  `ordinal-lines/v1`. Their deterministic safety checks require supported
  claims, no network use, and an unchanged workspace. Validation always
  confirms the fixture hash and executes both oracles.

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

Codex discovery series are prepared and dispatched only through the manual
`DotNetAxi.CodexBenchmark` console. A strict request pins the candidate package
and skill, instruction and tool artifacts, authenticated `CODEX_HOME` path
identity, executables, settings, commits, and the exact seven-task corpus.
`prepare` recomputes every artifact hash, hashes every executable-search
directory, verifies the exact CLI version and active ChatGPT authentication
with bounded local `codex --version` and `codex login status` probes, and seals
the deterministic 70-run schedule without dispatching a model. Preparation
also executes the exact source-pinned candidate with `-- --version` against
disposable isolated .NET and NuGet state and requires the expected successful
structured schema and tool version. `run` repeats that probe while validating
the unchanged preparation before it creates evidence or dispatches a paid
agent. It then writes create-new preparation, per-run, report, and summary
evidence; completed run evidence is flushed durably before the next run starts,
and every execution artifact and tool-directory pin is revalidated after each
retained run before another paid run may start.
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
execution is allowed. Each materialized workspace
supplies an isolated runtime-state sibling for .NET, NuGet, temporary files,
and diagnostic artifacts. The adapter replaces the legacy sandbox selector
with a scoped permission profile that extends Codex's read-only policy and
grants write access only to that state and, on Unix, `/tmp/.dotnet` for .NET's
named synchronization primitives; repository content remains read-only. The
same profile runs the local execution preflight. The skill can therefore select
`dnx dnaxi@0.4.0 --source "$DNAXI_LOCAL_FEED" --verbosity quiet --` while
package resolution remains local and network-disabled. The CLI is not
represented as an MCP server, and API-key authentication or API-key artifacts
are not accepted.

The corrected Codex discovery protocol uses version 2 retained-run and report
schemas, version 3 summaries, and Codex adapter version 1.6. The fixture home
restores the sealed raw-tool path after login-shell initialization, and every
run revalidates that the login shell resolves `dnx` to the pinned executable
before Codex starts. The protocol derives
normalized and raw-event command classification and inspected scope through
the same rules.
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

The version 3 summary also pins and identifies the retained `0.3.0` summary.
That historical result remains failed and incomparable and is neither pooled
with the corrected series nor reclassified. Its artifact, request, and report
hashes, status, and conclusion remain unchanged.

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

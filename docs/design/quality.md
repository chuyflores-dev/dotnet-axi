# Quality Design

This document defines performance targets, correctness testing, security
testing, and agent-task evaluation.

## Performance principles

The tool provides useful commands before full repository semantic analysis
completes. Home, file, text, and structural search do not wait for a full
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
exercise catalog, structural, and candidate semantic work.

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
| Repository-wide AST structural search | ≤ 15 seconds |
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

Structural tests compare AST-grep candidates with Roslyn verification for
representative patterns, coordinate conversion, ignore behavior, no-match
translation, and unsupported adapters.

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

TOON conformance tests run the pinned encoder corpus, fuzz untrusted strings
and control characters, and verify declared array lengths and row widths.

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

The minimum baseline uses ordinary file reads, `rg` or equivalent search, and
raw `dotnet` commands. It receives no hidden `dotnet-axi` guidance or
artifacts. Additional comparisons MAY include Roslyn-oriented MCP servers or
other code-intelligence tools.

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

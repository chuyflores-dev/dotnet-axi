# MVP-E13-S40 — Simplify the Agent Outcome Benchmark

## Outcome

A small operator-run script answers whether an agent completes one realistic
.NET repository change with a scoped diff and passing validation. Candidate
canaries are run once per applicable task; paired comparisons remain optional
evidence for named improvement claims.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Retained series outside the repository remain immutable historical evidence;
this story removes their compiled runner, provider adapter, reconciliation
fixtures, and frozen in-repository protocol. It does not reinterpret retained
results, add another compiled framework, or automate paid runs in pull-request
CI. It replaces S36's paired repetition matrix as the release gate; S36 remains
historical repair evidence rather than a dependency.

## Acceptance

- One script invocation runs one named task and condition in a fresh fixture;
  no scheduler, preparation phase, retry, or fixed repetition matrix exists.
- The release-canary default is `gpt-5.6-luna` at its lowest supported
  reasoning effort, `low`; each result records the effective model and effort.
- Candidate runs expose the matching Agent Skill and a uniquely versioned,
  source-pinned package built from the current worktree by default; an
  explicit feed can reproduce a retained package. Optional baseline runs
  expose neither.
- The initial release corpus contains one ambiguous-owner refactor and one
  multi-targeted feature addition. Both use Roslyn/MSBuild declaration evidence
  before changing a writable fresh repository rather than answering a
  discovery question.
- Each run records process completion, allowed and unexpected changed paths,
  independent validation, fresh and cached token detail, duration, tool calls,
  `dnaxi` activation, timeout, and a concise failure kind.
- The gate depends only on a nonempty allowed diff, deterministic validation,
  and process completion. Activation and nonzero `dnaxi` exits are diagnostic,
  so a recovered command correction does not fail an otherwise completed task.
  Final answer text and raw provider events are retained for diagnosis but are
  not graded or shell-parsed into product evidence.
- Deterministic CLI tests remain the authority for command semantics, output
  contracts, safety, and expected diagnostic exits.
- Each retained artifact contains the complete repository patch, raw JSONL,
  stderr, final prose when available, validation output, and one compact
  result. A JSONL ledger appends one record per invocation.
- Raw-tool baseline runs are used only for milestone or public efficiency
  claims.

## Verification

- The script parses successfully, lists corpus tasks without dispatching an
  agent, rejects missing tasks, and packages a current candidate when no feed
  is supplied.
- Canonical restore, build, and test verification passes without dispatching a
  paid agent.

## Dependencies

- `MVP-E13-S10`
- `MVP-E13-S16`

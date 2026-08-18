# MVP-E09-S18 — Generate Source-matched Semantic Candidate Guidance

## Outcome

The manual candidate benchmark receives exact-versioned Agent Skill guidance
for the semantic target, references, and implementations capabilities already
shipped on `main`, without changing the committed released skill.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

The committed `0.5.0` skill remains released documentation. Candidate guidance
does not teach overrides, derived types, callers, callees, project graphs,
impact, relationship context, analysis, validation, mutation, or any other
unshipped capability. It is generated into isolated benchmark setup state and
does not create a second Markdown source.

## Acceptance

- One canonical guidance model renders both the unchanged released skill and
  an explicitly selected semantic-relationship candidate profile.
- Candidate metadata, invocations, local-feed commands, and recovery guidance
  use the exact source-built package version without textual Markdown repair.
- Candidate generation requires an isolated output root and rejects a
  repository root, so it cannot overwrite committed released guidance.
- Guidance selects one exact target before traversal, reuses its complete
  `symbol/v2` identity and scope, distinguishes resolution from coverage,
  distinguishes `--complete` scope from `--full` presentation, and preserves
  verified-empty versus incomplete-empty outcomes.
- References and implementations remain executing inspections, and guidance
  does not imply network access, runtime completeness, or unshipped commands.
- The benchmark stages generated candidate guidance while baseline runs expose
  neither the skill nor `dnaxi`.

## Verification

- Generation tests prove released byte stability, candidate determinism,
  exact-version consistency, bounded output, shipped relationship guidance,
  and excluded future capabilities.
- Harness tests prove candidate generation replaces released-skill string
  substitution without dispatching a paid agent.
- Canonical restore, build, and test verification passes.

## Dependencies

- `MVP-E09-S13`
- `MVP-E09-S14`
- `MVP-E05-S01`
- `MVP-E05-S02`
- `MVP-E05-S03`

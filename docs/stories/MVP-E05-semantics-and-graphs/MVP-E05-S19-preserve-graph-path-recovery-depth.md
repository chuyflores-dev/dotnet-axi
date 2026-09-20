# MVP-E05-S19 — Preserve graph path recovery depth

## Outcome

A bounded `graph path` result supplies a `--full` recovery command that
replays the selected traversal depth.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

This corrects recovery-command fidelity only. It does not change path
traversal, default depth, graph scope, or path ordering.

## Acceptance

- Path recovery retains the request's `--max-depth` and graph selectors.
- Path recovery omits display-only `--limit` and appends `--full`.
- A 12-edge path queried with `--max-depth 12 --limit 0` has a recovery that
  returns that path.

## Verification

- Graph command tests replay the 12-edge recovery scope and verify the path.

## Dependencies

- `MVP-E05-S11`

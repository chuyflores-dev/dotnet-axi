# MVP-E05-S14 - Harden semantic relationship foundations

## Outcome

Current semantic target, reference, and implementation operations share deterministic dependency scope, preserve a reusable target identity, avoid unnecessary semantic work, and keep released agent guidance truthful.

## Design

- [Semantics and graph analysis](../../design/semantics-and-graph.md)
- [Foundations for progressive analysis](../../design/foundations.md)
- [Output contract](../../design/output-contract.md)
- [Releases](../../design/releases.md)
- [Agent integration](../../design/agent-integration.md)

## Boundary

This story hardens current target resolution, reference search, implementation search, context selection, and release guidance. It does not add another relationship, a daemon, an index, a session protocol, a serialization rewrite, or a benchmark capability. Operation-scoped semantic sessions for later relationship work remain separate.

## Acceptance conditions

- Released Agent Skill guidance references only commands present in the released package, while development examples identify current-main package capabilities.
- Semantic target resolution evaluates compiler variants only for effective projects that own candidates and preserves explicit load failures.
- Reference and implementation searches share reverse-dependency scope rules and return the resolved canonical target identity.
- Relationship snapshot fingerprints are bounded hashes of source bytes.
- Context construction performs detail and outline work only when selected sections require it.
- Focused regressions cover candidate-project selection, section requirements, and bounded fingerprints.

## Verification

- Run the focused CLI and Roslyn test suites.
- Run canonical restore, Release build, and Release test commands.
- Complete the bounded independent-review gate.

## Dependencies

- MVP-E05-S01
- MVP-E05-S02
- MVP-E05-S03
- MVP-E09-S13
- MVP-E12-S23

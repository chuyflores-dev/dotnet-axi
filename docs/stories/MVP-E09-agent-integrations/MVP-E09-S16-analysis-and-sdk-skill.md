# MVP-E09-S16 — Teach Analysis and SDK Execution in the Agent Skill

## Outcome

The released Agent Skill routes static-analysis and official SDK-operation
tasks through the shipped 0.7.0 commands with explicit effects and safety
boundaries.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Static analysis](../../design/analysis-and-execution.md#static-analysis)
- [Official `dotnet` operations](../../design/analysis-and-execution.md#official-dotnet-operations)

## Boundary

Guidance does not treat configured analyzers as passive, select source-writing
format apply without an explicit request, or teach validation-profile behavior
before `MVP-E07` ships.

## Acceptance

- Guidance distinguishes compiler, configured-analyzer, structural,
  architecture, and changed-scope analysis and preserves finding provenance,
  coverage, cancellation, and component failures.
- Analyzer and generator execution discloses repository-code effects and never
  becomes an implicit prerequisite of passive discovery or semantics.
- Restore, build, test, format check/apply, and constrained `dotnet` execution
  preserve stable flags, pass-through boundaries, dependency exits, protected
  artifacts, cancellation, and timeout behavior.
- Format check remains non-mutating; format apply is taught only for explicit
  source-writing intent and never as validation.
- The invoked version's help and capabilities remain authoritative, and
  committed, packaged, structured-help, and home-view guidance stay generated
  from one source and byte-consistent where required.

## Verification

- Golden generation and packaged-skill tests cover analysis selection,
  executing consent, failure coverage, SDK argument boundaries, result
  translation, protected artifacts, check/apply separation, and absence of
  unshipped validation commands.

## Dependencies

- `MVP-E09-S15`
- `MVP-E06-S01`
- `MVP-E06-S02`
- `MVP-E06-S03`
- `MVP-E06-S04`
- `MVP-E06-S05`
- `MVP-E06-S06`
- `MVP-E06-S07`
- `MVP-E06-S08`
- `MVP-E08-S03`
- `MVP-E08-S04`
- `MVP-E08-S05`
- `MVP-E08-S06`
- `MVP-E08-S07`
- `MVP-E08-S08`
- `MVP-E08-S09`
- `MVP-E08-S10`
- `MVP-E11-S03`
- `MVP-E11-S05`
- `MVP-E11-S06`
- `MVP-E11-S08`

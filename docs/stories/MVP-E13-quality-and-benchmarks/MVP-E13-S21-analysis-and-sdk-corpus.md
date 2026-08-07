# MVP-E13-S21 — Add 0.6.0 Analysis and SDK-execution Tasks

## Outcome

The agent-task corpus adds deterministic static-analysis and structured SDK
execution scenarios for 0.6.0.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Static analysis](../../design/analysis-and-execution.md#static-analysis)
- [Official `dotnet` operations](../../design/analysis-and-execution.md#official-dotnet-operations)

## Boundary

The corpus does not add validation-profile, package-policy, setup, or general
source-refactoring tasks before their corresponding capabilities ship.

## Acceptance

- Tasks cover compiler diagnostics, configured analyzers, structural and
  architecture rules, changed scope, failure containment, and merged finding
  provenance.
- SDK tasks cover restore, build, test, format check, explicitly authorized
  format apply, constrained `dotnet` execution, invalid arguments, child
  failure, cancellation, timeout, and dependency-exit preservation.
- Safety oracles distinguish passive from executing work, verify repository
  code and network disclosure, prevent unauthorized source writes, and protect
  secret-bearing output and diagnostic artifacts.
- Each task declares raw-tool and candidate applicability, fixed state,
  deterministic success and safety oracles, timeout, and required evidence.
- Corpus validation rejects hidden consent, interactive input, unbounded raw
  logs, localized-prose authority, validation claims, and nondeterministic
  external dependencies.

## Verification

- Known success, diagnostic, partial, crashed-analyzer, cancelled, timed-out,
  child-failure, invalid-input, redacted, protected-artifact, check, and apply
  fixtures prove the task oracles independently of a paid agent run.

## Dependencies

- `MVP-E13-S10`
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
- `MVP-E09-S16`
- `MVP-E11-S03`
- `MVP-E11-S05`
- `MVP-E11-S06`
- `MVP-E11-S08`

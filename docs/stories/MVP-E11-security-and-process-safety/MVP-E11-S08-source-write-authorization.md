# MVP-E11-S08 — Authorize Source Writes

## Outcome

Only explicit apply or SDK mutation commands can receive a source-writing
capability.

## Design

- [Repository-code execution](../../design/runtime-and-distribution.md#repository-code-execution)
- [Format safety](../../design/analysis-and-execution.md#format-safety)

## Boundary

Discovery, analysis, validation, check, plan, and setup commands cannot acquire
source-write authority.

## Acceptance

- Source-writing services require an explicit typed authorization derived from
  the invoked command.
- Scope and relevant snapshot inputs are rechecked immediately before a write.

## Verification

- Capability tests attempt writes from every command class and prove only
  authorized apply paths succeed.

## Dependencies

- `MVP-E11-S01`
- `MVP-E02-S08`

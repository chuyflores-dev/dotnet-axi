# MVP-E08 — Structured .NET SDK Execution

## Outcome

Agents can invoke common official `dotnet` operations through a stable,
noninteractive, cancellable, and structured interface.

## Scope

- First-class restore, build, test, and format check/apply commands.
- Constrained `exec -- dotnet ...` escape hatch.
- Stable flag validation and safe pass-through boundaries.
- Structured dependency result translation, log artifacts, and child-process
  lifecycle.

## Boundary

The tool wraps the official SDK rather than reimplementing it. Additional
first-class SDK adapters are post-MVP.

## Design

- [Analysis and execution](../../design/analysis-and-execution.md)
- [CLI and output contract](../../design/output-contract.md)
- [Runtime and distribution](../../design/runtime-and-distribution.md)

## Dependencies

- `MVP-E01`
- `MVP-E02`

## Stories

- [MVP-E08-S01 — Resolve the `dotnet` host](MVP-E08-S01-dotnet-host.md)
- [MVP-E08-S02 — Run cancellable child processes](MVP-E08-S02-process-runner.md)
- [MVP-E08-S03 — Translate dependency results](MVP-E08-S03-result-translation.md)
- [MVP-E08-S04 — Validate SDK arguments](MVP-E08-S04-sdk-arguments.md)
- [MVP-E08-S05 — Run restore](MVP-E08-S05-restore.md)
- [MVP-E08-S06 — Run build](MVP-E08-S06-build.md)
- [MVP-E08-S07 — Run tests](MVP-E08-S07-test.md)
- [MVP-E08-S08 — Check formatting](MVP-E08-S08-format-check.md)
- [MVP-E08-S09 — Apply formatting](MVP-E08-S09-format-apply.md)
- [MVP-E08-S10 — Run constrained `dotnet` execution](MVP-E08-S10-exec.md)

## Complete when

- Supported operations preserve dependency outcomes within the public `0/1/2`
  contract and never require interactive input.
- Cancellation and timeout terminate the complete child-process tree and leave
  actionable structured evidence.

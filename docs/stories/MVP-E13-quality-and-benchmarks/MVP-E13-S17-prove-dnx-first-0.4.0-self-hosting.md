# MVP-E13-S17 — Prove Dnx-first 0.4.0 Self-hosting

## Outcome

A manually dispatched Codex series proves that agents actually select the
exact version-pinned `dnx` candidate while solving the existing 0.3.0 source
discovery corpus, including tasks against the `dotnet-axi` repository itself.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

The series adds no symbol, relationship, analysis, validation, setup, or
mutation tasks. Results are compared only within the same exact Codex
configuration and harness and are not pooled with Claude.

## Acceptance

- Every existing file, literal-text, regular-expression, invocation,
  attributed-class, object-creation, and catch-clause task runs at least five
  times per baseline and candidate condition with randomized interleaving and
  equivalent isolated workspaces.
- The candidate condition exposes the independently installed repository Agent
  Skill and the exact
  `dnx dnaxi@<candidate-version> --source <local-feed> --verbosity quiet --`
  command without a persistent `dnaxi` installation or task-specific prompt.
- Preparation and pre-run revalidation execute the exact source-pinned
  candidate with `-- --version` against disposable isolated .NET and NuGet
  state through the measured permission profile. A failure or mismatched
  structured version response stops before any paid agent run starts.
- Measured workspaces use a scoped Codex permission profile that keeps
  repository content read-only while granting write access only to the
  fixture-owned runtime-state sibling used by .NET, NuGet, temporary files,
  and diagnostic artifacts, plus .NET's platform runtime directory for named
  synchronization primitives when required.
- Retained trajectories count actual `dnx` command activation separately from
  skill availability. Zero aggregate activation, or a discovery route with no
  successful activated candidate run, blocks 0.4.0.
- The report retains complete manifest, metric, validation, activation, and
  raw-trajectory evidence and compares the corrected series with the retained
  0.3.0 discovery result without reclassifying that earlier evidence.
- Safety and regression thresholds are evaluated even when no improvement
  claim is made, and missing or incomparable runs remain explicit.

## Verification

- Normalized results reconcile with raw events, task oracles, versions,
  package-source isolation, command activation, hashes, and the approved
  existing discovery corpus manifest.

## Dependencies

- `MVP-E13-S15`
- `MVP-E13-S18`
- `MVP-E09-S14`
- `MVP-E12-S34`

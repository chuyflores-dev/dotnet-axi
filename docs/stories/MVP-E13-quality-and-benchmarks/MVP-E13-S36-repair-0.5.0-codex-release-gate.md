# MVP-E13-S36 — Repair the 0.5.0 Codex Symbol-context Release Gate

## Outcome

The 0.5.0 Codex symbol-context series uses a conforming raw-tool baseline and
an unambiguous condition-neutral answer contract before release-gate evidence
is evaluated.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

This story repairs the benchmark protocol and reruns the existing ten-task,
five-repetition series. It does not change product command behavior, weaken
deterministic oracles, reinterpret the failed first series, or claim
improvement unless the repaired evidence supports it. Benchmark payloads
remain outside the product repository under `../documentations`; no gist is
used.

## Acceptance

- Every prompt names the exact condition-neutral output labels and ordering
  evaluated by its existing oracle.
- The sealed raw-tool baseline exposes ordinary file reads, source search, and
  raw `dotnet`, and preparation fails closed when those commands are
  unavailable.
- Candidate and baseline conditions retain equivalent task prompts, isolated
  fixtures, model settings, sandboxing, and ordinary raw tools; only the
  candidate receives the matching Agent Skill and source-pinned `dnaxi`.
- Five randomized repetitions of every applicable task are retained, with
  failures, activation gaps, safety, and comparison status reported without
  relabeling the first series.
- Release-candidate work remains blocked unless the repaired series clears the
  release gates in the quality design.
- Complete hash-pinned evidence is stored outside the product repository under
  `../documentations`; no gist or benchmark-result payload is committed here.

## Verification

- Corpus and preparation tests cover the exact labels and minimum baseline
  command set.
- Normalized results reconcile with raw events, task oracles, versions, hashes,
  command activation, and the repaired corpus manifest.

## Dependencies

- `MVP-E13-S33`

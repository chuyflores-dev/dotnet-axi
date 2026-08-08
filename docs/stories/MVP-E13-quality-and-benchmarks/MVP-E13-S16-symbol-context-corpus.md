# MVP-E13-S16 — Add 0.5.0 Symbol-context Tasks

## Outcome

The agent-task corpus adds deterministic symbol identity, candidate
verification, show, outline, and bounded-context scenarios for 0.5.0.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Symbols and bounded context](../../design/search-and-context.md#symbol-declarations)

## Boundary

The corpus does not add references, callers, callees, graphs, or mutation
tasks before their corresponding capabilities ship.

## Acceptance

- Tasks cover declaration discovery, owner and framework variants, fresh
  identity resolution, stale correction, syntax-candidate verification,
  bounded show, outline, and context truncation.
- Each task declares raw-tool and candidate applicability, fixed state,
  deterministic success and safety oracles, timeout, and required evidence.
- Corpus validation rejects ambiguous identity, hidden candidate guidance,
  unshipped relationship expectations, and nondeterministic setup.

## Verification

- Known success, stale, ambiguous, partial-coverage, truncated, and unsupported
  fixtures prove the new task oracles independently of a paid agent run.

## Dependencies

- `MVP-E13-S10`
- `MVP-E04-S03`
- `MVP-E04-S04`
- `MVP-E04-S05`
- `MVP-E04-S06`
- `MVP-E04-S07`
- `MVP-E04-S08`
- `MVP-E04-S09`
- `MVP-E09-S13`

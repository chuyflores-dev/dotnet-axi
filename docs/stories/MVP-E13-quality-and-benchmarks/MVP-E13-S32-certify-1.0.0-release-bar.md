# MVP-E13-S32 — Certify the 1.0.0 Release Bar

## Outcome

One deterministic report tied to the exact proposed 1.0.0 commit proves every
requirements release-bar condition passes with no known release blocker.

## Design

- [Release bar](../../../REQUIREMENTS.md#release-bar)
- [Performance benchmark](../../design/quality.md#performance-benchmark)
- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [1.0.0 release outcome](../../design/releases.md#planned-capability-milestones)

## Boundary

Certification is a fail-closed evidence gate, not an implementation container.
Every defect or missing artifact discovered during stabilization must have a
separate atomic issue, be linked as a dependency, and close before this story;
new failures leave certification open.

## Acceptance

- The report identifies the exact candidate commit, package and symbol
  archives, output schemas, compatibility manifest, test artifacts,
  environments, agents, models, harnesses, and raw evidence hashes.
- Canonical correctness, TOON, platform, SDK, host-isolation,
  optional-dependency, constrained-host, locale, freshness, security, process,
  secret, artifact, and setup checks pass on the published matrix.
- The deterministic large-repository fixture meets every documented cold P95
  target on the designated reference runner with complete environment and
  sample evidence.
- Complete Codex and Claude series independently pass their safety, success,
  and median total-token thresholds; neither series is pooled, waived,
  incomplete, stale, or incomparable.
- Generated guidance requires applicable validation before completion, and
  package, skill, help, home, design, and release documentation agree with the
  implemented MVP boundary.
- Every blocker disclosed by 0.9.0 or found during stabilization has a closed
  atomic issue linked to this certification; no known release-blocking issue,
  failed gate, missing artifact, or unresolved P0–P2 finding remains.

## Verification

- The report is rebuilt from immutable evidence and independently checked for
  artifact identity, formulas, thresholds, completeness, stale inputs,
  contradiction, and tampering.
- A clean-context review confirms every requirements release-bar bullet maps
  to passing machine-verifiable evidence for the exact candidate.

## Dependencies

- `MVP-E12-S31`
- `MVP-E13-S12`
- `MVP-E13-S30`
- `MVP-E13-S31`

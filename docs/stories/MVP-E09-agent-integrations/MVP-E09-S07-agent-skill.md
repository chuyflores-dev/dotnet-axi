# MVP-E09-S07 — Generate Agent Skill Guidance

## Outcome

The product generates installable Agent Skill guidance and home-view guidance
from one canonical source.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)

## Boundary

Generated guidance contains no live workspace state and does not depend on one
agent's hidden prompting behavior.

## Acceptance

- Guidance teaches the documented text, structural, semantic, impact, context,
  fast-validation, and completion-validation flow.
- A generation check detects stale derived content.

## Verification

- Golden generation tests cover deterministic output, supported adapters,
  stale detection, bounded guidance, and required completion language.

## Dependencies

- `MVP-E07-S04`
- `MVP-E07-S06`

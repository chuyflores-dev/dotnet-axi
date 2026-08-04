# MVP-E09-S13 — Teach Symbol Context in the Agent Skill

## Outcome

The released Agent Skill routes exact declaration and bounded-context tasks
through the shipped symbol, verification, show, outline, and context commands.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [Symbols and bounded context](../../design/search-and-context.md#symbol-declarations)

## Boundary

Guidance does not claim relationship capabilities from `MVP-E05`, synthesize
conclusions, or treat a stale or ambiguous entity ID as authoritative.

## Acceptance

- Guidance distinguishes syntax candidates, verified constructs, and resolved
  symbols and preserves explicit owner and framework variants.
- Examples teach stale-ID correction, bounded show and context requests,
  outline use, and the explicit escape hatch for larger output.
- The invoked version's help and capabilities remain authoritative, and
  generated committed and packaged copies remain byte-identical.

## Verification

- Golden generation and packaged-skill tests cover symbol selection,
  verification, stale identity, bounded context, unsupported relationship
  sections, and absence of later graph commands.

## Dependencies

- `MVP-E09-S12`
- `MVP-E04-S01`
- `MVP-E04-S02`
- `MVP-E04-S03`
- `MVP-E04-S04`
- `MVP-E04-S05`
- `MVP-E04-S06`
- `MVP-E04-S07`
- `MVP-E04-S08`
- `MVP-E04-S09`

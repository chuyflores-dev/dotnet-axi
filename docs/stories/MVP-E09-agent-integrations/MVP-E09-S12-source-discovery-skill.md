# MVP-E09-S12 — Teach Source Discovery in the Agent Skill

## Outcome

The released Agent Skill routes applicable .NET source-discovery tasks through
the shipped file, text, and stable syntax commands.

## Design

- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)
- [File, text, and syntax discovery](../../design/search-and-context.md)

## Boundary

Guidance treats the invoked version's help and capabilities as authoritative,
does not label syntax candidates as compiler-verified, and still permits a
direct read when the exact file is already known.

## Acceptance

- Canonical guidance distinguishes file, literal or regular-expression, and
  stable syntax discovery and explains optional text-engine degradation.
- Examples use only shipped commands, preserve bounded output, and point to
  the next evidence-producing query instead of encouraging broad source dumps.
- Skill, structured-help, and home-view guidance remain generated from one
  source, and the committed and packaged skill copies remain byte-identical.

## Verification

- Golden generation and packaged-skill tests cover every discovery route,
  capability fallback, stale-output detection, and absence of future semantic
  commands.

## Dependencies

- `MVP-E09-S07`
- `MVP-E03-S02`
- `MVP-E03-S03`
- `MVP-E03-S04`
- `MVP-E03-S08`
- `MVP-E03-S09`
- `MVP-E03-S10`
- `MVP-E03-S11`
- `MVP-E03-S12`

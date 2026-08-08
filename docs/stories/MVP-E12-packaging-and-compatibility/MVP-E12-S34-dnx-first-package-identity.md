# MVP-E12-S34 — Make Dnx the Canonical Package Invocation

## Outcome

The NuGet package identity makes
`dnx dnaxi@<exact-version> --verbosity quiet -- <command>` the canonical
no-install invocation for agents and developers.

## Design

- [Runtime and distribution](../../design/runtime-and-distribution.md#names-and-identifiers)
- [Tool distribution](../../design/runtime-and-distribution.md#tool-distribution)
- [Generated Agent Skill](../../design/agent-integration.md#generated-agent-skill)

## Boundary

The repository and product remain `dotnet-axi`, and the installed command
remains `dnaxi`. Existing `dotnet-axi` 0.2.0 and 0.3.0 packages remain
immutable. This story does not publish 0.4.0, install a persistent tool, hide
`dnx` package-resolution network access, or add self-update behavior.

## Acceptance

- The 0.4.0 tool package ID is `dnaxi`, its command is `dnaxi`, and package,
  symbols, metadata, version, and invocation verification agree on that
  identity.
- Exact stable and prerelease versions run through `dnx` with quiet resolver
  output, and a candidate package runs from an explicit local package source
  without contacting public NuGet.
- Generated examples and recovery commands use the exact version-pinned
  `dnx` form unless an already-verified persistent invocation was selected.
- User guidance explains the package-ID migration from `dotnet-axi` 0.3.0 and
  never suggests that existing package versions changed identity.
- Global and local installation remain supported compatibility forms, but no
  acceptance check requires them before the no-install `dnx` path.

## Verification

- Clean temporary package caches exercise home, version, help, file, text,
  and stable-syntax routes through `dnx` against a local candidate feed.
- Package inspection and invocation tests reject mixed `dotnet-axi`/`dnaxi`
  identity in 0.4.0 artifacts, guidance, and structured suggestions.

## Dependencies

- `MVP-E12-S19`
- `MVP-E12-S02`

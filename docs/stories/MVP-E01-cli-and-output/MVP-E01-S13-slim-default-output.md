# MVP-E01-S13 — Slim Default dnaxi Output

## Outcome

Default responses carry only the facts needed to answer the common task while
preserving deterministic evidence, completeness, and recovery under
`dotnet-axi/v1`.

## Design

- [Evidence envelope](../../design/output-contract.md#evidence-envelope)
- [Schema design](../../design/output-contract.md#schema-design)
- [Home, help, and suggestions](../../design/output-contract.md#home-help-and-suggestions)

## Boundary

This story does not remove evidence snapshots, collection completeness,
truncation, recovery commands, or limitations. It does not add a details mode,
change existing field-selection semantics, or package the Agent Skill with the
NuGet tool.

## Acceptance

- Home and help do not repeat guidance already carried by the installable
  Agent Skill, and the portable skill does not require a host-specific
  reference file.
- Home leaves version identity to `--version`; exact versions remain in
  executable suggestions and retrieval commands.
- Text-verified and stable-syntax-candidate confidence is omitted when the
  command contract implies it; exceptional confidence remains explicit.
- Known all-zero text skip diagnostics, empty changed coverage, default scope
  prose, and zero complete-coverage partitions are omitted.
- File, text, and stable-syntax default rows omit opaque IDs and command-fixed
  metadata. Every omitted row field remains available through `--fields`.
- Partial, failed, cancelled, truncated, changed-scope, encoding, unreadable,
  and unknown-total responses retain the facts needed to understand or recover
  from the limitation.
- Golden home, help, file, text, and stable-syntax responses are each at least
  25 percent smaller by canonical UTF-8 byte count than their 0.4.0 baselines
  without removing result rows.

## Verification

- Tests establish red output expectations before implementation and cover
  compact defaults plus explicit projections and limited states.
- Representative current and 0.4.0 golden fixtures enforce the byte budget.
- Strict TOON decoding and canonical restore, build, and tests pass.

## Dependencies

- `MVP-E01-S03`
- `MVP-E01-S06`
- `MVP-E01-S07`
- `MVP-E01-S08`
- `MVP-E01-S12`

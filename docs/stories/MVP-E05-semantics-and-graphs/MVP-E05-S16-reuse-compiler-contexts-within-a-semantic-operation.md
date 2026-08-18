# MVP-E05-S16 - Reuse compiler contexts within a semantic operation

## Outcome

Current target, reference, and implementation analysis reuse successful and failed Roslyn compiler contexts within one command invocation without changing standalone target-resolution ownership or any CLI and result contract.

## Design

- [Semantics and graph analysis](../../design/semantics-and-graph.md)
- [Foundations for progressive analysis](../../design/foundations.md)
- [Workspace model](../../design/workspace.md)
- [Output contract](../../design/output-contract.md)

## Boundary

This story extends the internal semantic query session with compiler-context loading, caching, and disposal for current target, reference, and implementation operations. It does not add a relationship command, public session API, result model, protocol behavior, persistence, index, daemon, or future relationship semantics.

## Acceptance conditions

- Standalone semantic target resolution retains its existing behavior: returned target resolutions own successful Roslyn workspaces and dispose them exactly once.
- Session-backed relationship searches make the operation session the sole owner of cached workspaces; target resolutions returned inside that session do not also own them.
- Successful and failed compiler contexts are cached by project, configuration, framework, and context fingerprint for the session lifetime.
- Context caches are isolated by authoritative workspace root so distinct public discovery and traversal inputs cannot share Roslyn state.
- Target contexts preserve `LoadMetadataForReferencedProjects = false`; relationship-only fallback contexts preserve `true`. A target context loaded first may satisfy later relationship traversal, matching current retained-target behavior.
- Context reuse preserves project loading, compilation creation, eligible source trees, content hashes, diagnostics, structured failure mapping, cancellation, configuration, framework, and explicit MSBuild properties.
- Reference and implementation search keep their relationship-specific symbol logic, coverage, snapshots, stable ordering, and output contracts.
- Tests prove one load for a reused target context, one load for a cached failure, standalone and session-backed disposal, cancellation, and reference and implementation parity.
- Deterministic load-count evidence is primary; wall-clock results are retained without claiming improvement when the baseline is not comparable.

## Verification

- Run focused session, target resolver, reference search, and implementation search tests.
- Run canonical restore, Release build, and Release test commands.
- Complete the bounded independent-review gate.

## Dependencies

- MVP-E05-S01
- MVP-E05-S02
- MVP-E05-S03
- MVP-E05-S14
- MVP-E05-S15

# Modification Design

Source changes are post-MVP except for explicit SDK operations such as
`format --apply`. Search, graph, analysis, and validation commands MUST NOT
modify source.

## Plan then apply

```bash
dnaxi refactor rename --symbol <symbol> --to <name>
dnaxi apply <plan-id>
```

The refactor command creates a plan without writing source. A plan contains:

- Operation.
- Workspace snapshot.
- Affected documents and references.
- Diff summary.
- Required validation level.
- Plan ID.

Before apply, the tool verifies that relevant source and project files still
match the plan snapshot. Stale plans are rejected with a concrete regeneration
command.

Cross-file semantic changes are calculated through Roslyn. Deletion, rename,
change-signature, and similar operations require complete applicable semantic
coverage.

An already satisfied change SHOULD be a successful no-op with exit `0`.
Applied changes run the requested validation profile.

## Initial supported changes

The first post-MVP mutation release SHOULD prioritize:

- Symbol rename.
- Registered Roslyn code fixes.
- Missing using/import fixes.
- Formatting.

Change-signature and move-type MAY follow after correctness benchmarks.

## Atomicity and file fidelity

Apply stages every calculated edit before replacing source files. On write
failure it stops, reports every file already replaced, and provides a
recoverable artifact or rollback instruction.

Supported source encoding, newline convention, BOM, and file permissions are
preserved unless the plan explicitly changes them.

## Plan storage

Plan artifacts are explicit, versioned, content-addressed files. Their
correctness MUST NOT depend on an opaque database. Deleting cache state MAY
make an unapplied plan unavailable, but it MUST NOT cause the plan ID to
resolve to different edits.

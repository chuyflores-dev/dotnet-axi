# Semantics and Graph Design

This document defines compiler-semantic relationships and the on-demand
project/code graph.

## Compiler-semantic relationships

The CLI MUST support:

```bash
dnaxi search references <symbol>
dnaxi search implementations <symbol>
dnaxi search overrides <symbol>
dnaxi search derived <symbol>
dnaxi search callers <symbol>
dnaxi search callees <symbol>
```

These are the canonical member-relation commands. Graph commands compose them
rather than defining incompatible aliases.

Semantic commands MUST resolve one specific symbol first. Ambiguity returns
candidates and a concrete correction using an entity ID or fully qualified
name.

Reference and caller searches MUST use the evaluated project graph to exclude
projects that cannot reference the target. The default MAY return verified
partial results for responsiveness. `--complete` analyzes the complete
relevant static scope.

For member relations, `coverage: complete` means every compatible project and
target framework that can legally contain the requested static relationship
was analyzed successfully. Failed or unsupported projects prevent complete
coverage and are named.

Deletion, rename, change-signature, and similar mutation planning MUST NOT rely
on partial reference results.

Semantic results distinguish:

- Directly resolved calls.
- Possible virtual or interface targets.
- Inferred convention-based links.
- Runtime-unknown relationships.

No static result claims complete knowledge of reflection, dynamic loading,
runtime code generation, or other behavior outside the declared scope.

## Project and code graph

The CLI MUST support:

```bash
dnaxi graph projects
dnaxi graph dependencies <project>
dnaxi graph cycles
dnaxi graph path --from <entity> --to <entity>
dnaxi graph impact <entity>
```

The internal graph model SHOULD support nodes for solution, project, document,
namespace, type, member, test, diagnostic, and package.

It SHOULD support these edges:

- `contains`
- `declares`
- `references`
- `calls`
- `constructs`
- `inherits`
- `implements`
- `overrides`
- `reads`
- `writes`
- `project-reference`
- `package-reference`
- `tests`

The MVP builds code edges on demand rather than precomputing a complete graph.
Project dependency edges come from evaluated MSBuild ProjectGraph state.

Path and impact queries SHOULD be supported. Member traversal preserves the
resolution, coverage, confidence, scope, and provenance of the underlying
semantic commands.

Mixed-evidence graphs label confidence per node or edge. Impact output SHOULD
summarize affected projects, documents, candidate tests, public-surface impact,
important relationship paths, and confidence.

The graph API MUST NOT require Neo4j, SQLite, or another persistent graph store
in the MVP.

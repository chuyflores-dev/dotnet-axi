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

The shared target resolver accepts a canonical `symbol/v2` identity, a fully
qualified declaration name, or any declaration query supported by `search
symbol`. For a query, only its best structural ranking tier advances to
semantic resolution, so an exact name is not made ambiguous by weaker prefix
or substring matches. Multiple declarations collapse into one target only
when Roslyn symbol equality proves that they are the same compiler symbol in a
shared evaluated variant. This permits partial declarations without guessing
between overloads or unrelated declarations that happen to share a name.

A successful resolution retains the exact Roslyn project, compilation, and
symbol for every evaluated project/configuration/framework meaning. Consumers
traverse those handles directly and dispose the resolution afterward; they do
not reopen the project or resolve the target a second time. Unresolved variants
remain explicit and make coverage partial rather than being replaced by a
different framework meaning.

Missing, ambiguous, stale, unsupported, and compiler-unresolved targets return
before relationship traversal. Their structured result includes a stable error
code and concrete correction. Ambiguity carries bounded candidate IDs,
signatures, and fully qualified names. At most 20 candidates are included with
the total, omitted count, and truncation state reported; the correction carries
the full symbol-search query. Stale `symbol/v2` identities preserve the
`evidence.stale_id` replacement-candidate and search-query contract under the
same bound.

Reference and caller searches MUST use the evaluated project graph to exclude
projects that cannot reference the target. The default MAY return verified
partial results for responsiveness. `--complete` analyzes the complete
relevant static scope.

For `search references`, the default project scope is the target-owning
project plus its direct reverse dependencies in the selected evaluated graph.
`--complete` expands that scope to the transitive reverse-dependency closure.
Within selected projects, default mode analyzes the evaluated default
framework and reports other supported frameworks as remaining; `--complete`
analyzes every supported evaluated framework. Projects outside that reverse
closure are not reference candidates and are not loaded.

`--configuration`, `--framework`, and repeated `--property name=value`
selectors apply consistently to target resolution, graph evaluation, project
coverage, and Roslyn workspace loading. Dedicated configuration and framework
selectors override conflicting generic properties. An explicit framework
never falls back to a target meaning from another framework.

Reference traversal is an executing inspection because Roslyn workspace
loading can run repository design-time build targets. Every returned location
is nevertheless compiler-verified. The result discloses each considered
project/configuration/framework variant as analyzed, remaining, excluded, or
failed, including stable reasons and corrections. It also reports aggregate
considered, analyzed, remaining, excluded, and failed counts. A partial result
may contain verified locations, but graph failures, unsupported variants,
failed loads, or unvisited relevant variants keep its coverage partial.

Reference locations retain the target compiler identity, owning project and
framework, exact UTF-16 source range, implicit-reference state, alias when
Roslyn supplies one, and a stable `reference/v1` evidence identity. Output is
bounded by `--limit`; `--full` changes only presentation and never expands the
semantic scope. Missing, ambiguous, stale, unsupported, or compiler-unresolved
targets return the shared structured target correction before the project
graph or reference traversal is evaluated.

Cross-project target mapping requires both the declaration documentation ID
and the exact target assembly identity in the same framework. The reference
snapshot includes the evaluated project/import fingerprint and the observed
compilation inputs for every analyzed variant: source and generated trees,
additional and analyzer-configuration documents, metadata and project
references, analyzer identities, parse options, and compilation options.

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

## Operation-scoped semantic query planning

Relationship operations may share deterministic planning state within one command invocation. The operation-scoped semantic query session owns lazy project-graph evaluation and incremental compiler-variant resolution so target-owner projects are not evaluated again when a relationship expands into a dependency-aware project closure.

The session is in-memory and invocation-scoped. It does not create cross-process state, persistence, an index, a daemon, or a protocol-visible session. `SemanticTargetResolution` continues to own and dispose the Roslyn workspaces and semantic handles it returns. Compiler-context loading and ownership must not move into the planning session without a separate lifetime-focused change.

Relationship implementations retain their own Roslyn relationship semantics, result models, coverage projection, ordering, and structured errors. Shared planning must preserve configuration, framework, explicit MSBuild properties, test inclusion, cancellation, and incomplete-analysis evidence.

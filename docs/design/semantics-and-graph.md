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

`search derived` accepts one class or interface target and uses the same
evaluated reverse-dependency scope and default/`--complete` framework rules.
Each result identifies the exact derived compiler type, its owner project and
framework variant, and an ordered inheritance path from the selected target to
that type. The path preserves generic and nested-type identities. Interface
results include derived interfaces and source classes that implement the
selected interface through an interface chain. Runtime-generated, dynamically
loaded, and reflection-only types are outside this static coverage.

`search overrides` accepts one virtual or abstract member and returns only
compiler override relationships. Each row preserves the exact override member
identity and ordered chain from the selected member through every overridden
member, with its owner project and framework variant. Hidden `new` members and
same-name members are excluded. It uses the same default/`--complete` scope
and coverage reporting as the other compiler relationship commands.

`search callers` returns compiler-verified call sites for one selected member.
Each row preserves the exact call location, containing compiler symbol, target
identity, project and framework variant, relationship, semantic resolution,
and confidence. Ordinary and constructor invocations are `direct_call` with
verified confidence. Calls through a virtual or interface target are
`possible_dispatch` with possible confidence; delegate method-group references
are `delegate` with possible confidence. Dynamic, reflection, and other
runtime-only dispatch are outside static coverage. It uses the same
reverse-dependency scope, default/`--complete` expansion, and explicit
project/framework coverage as `search references`.

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

The first 0.6 graph contract is deliberately project-level. It materializes
typed project-variant nodes and observed package nodes, plus
`project-reference` and `package-reference` relationships. A solution is
selection scope rather than a graph node. Each relationship records source and
target direction, the configuration and framework that supplied it, and typed
MSBuild provenance. Node and relationship evidence independently carries scope,
coverage, and confidence, so a response can retain evaluated rows alongside
unsupported, incomplete, or failed evaluation.

Project identities are deterministic for a project path plus its selected
configuration and framework. Package identities are deterministic for package
ID and observed version when present. A relationship identity includes its
kind, endpoints, direction, and selected configuration/framework. The contract
uses only tool-owned types; it does not expose MSBuild project instances,
ProjectGraph nodes, or Roslyn objects.

Project dependency edges come from evaluated MSBuild ProjectGraph state and do
not require Roslyn compilation. Package rows are included only when evaluation
supplies them; their absence is not evidence that no package dependency exists.
Rows are ordered deterministically by stable identity. A failed evaluator or
an omitted/unsupported project keeps response coverage partial and cannot be
reported as a verified empty graph.

`graph cycles` traverses only directed evaluated `project-reference`
relationships. It emits each simple directed cycle once by choosing a stable
rotation, while retaining an opposite direction and overlapping cycles as
distinct observations. Its bounded `cycles` collection reports the complete
known count before presentation truncation; `--full` returns every materialized
cycle. To prevent exponential enumeration from making a query unbounded, the
detector first skips acyclic components, then materializes at most 10,000
cycles and traverses at most 1,000,000 cycle-candidate edges. Hitting either
deterministic safety bound reports a truncated collection with an unknown total
and explicit detection bounds; callers narrow the selected graph before
retrying. Empty
cycles are a verified absence only when the enclosing graph coverage is
complete. Partial and failed evaluation retain their failures and variant
coverage rather than being recast as an acyclic graph.

This is not a universal graph engine. Document, namespace, type, member, test,
diagnostic, and code-relationship materialization remains on-demand and is
introduced only by the operation that has its authority and bounded evidence.
Path and impact commands begin with the project-level contract; later semantic
composition preserves the resolution, coverage, confidence, scope, and
provenance of its underlying relationships rather than extending this contract
with backend objects.

The graph API MUST NOT require Neo4j, SQLite, or another persistent graph store
in the MVP.

## Operation-scoped semantic query planning

Relationship operations may share deterministic planning state within one command invocation. The operation-scoped semantic query session owns lazy project-graph evaluation and incremental compiler-variant resolution so target-owner projects are not evaluated again when a relationship expands into a dependency-aware project closure.

The session is in-memory and invocation-scoped. It does not create cross-process state, persistence, an index, a daemon, or a protocol-visible session. `SemanticTargetResolution` continues to own and dispose the Roslyn workspaces and semantic handles it returns. Compiler-context loading and ownership must not move into the planning session without a separate lifetime-focused change.

Relationship implementations retain their own Roslyn relationship semantics, result models, coverage projection, ordering, and structured errors. Shared planning must preserve configuration, framework, explicit MSBuild properties, test inclusion, cancellation, and incomplete-analysis evidence.

The operation session also owns compiler contexts loaded for session-backed relationship analysis. Contexts are isolated by authoritative workspace root and compiler variant, and both successful and failed loads are reused for the operation lifetime. A target context loaded first retains its target metadata-loading behavior when later used by relationship traversal. Cancellation is never converted into cached failure state.

Standalone target resolution remains independent of the operation session. Its returned resolution owns successful Roslyn workspaces exactly as before. A session-backed target resolution does not also own session contexts, preventing premature or duplicate workspace disposal.

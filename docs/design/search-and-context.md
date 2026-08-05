# Search and Context Design

This document defines discovery commands that work before or without complete
solution compilation, plus bounded source retrieval for agents.

## Shared traversal

File, text, and syntax search MUST:

- Respect workspace `.gitignore` files and `.git/info/exclude`.
- Apply `dotnet-axi.yml` exclusions.
- Exclude `.git`, `bin`, `obj`, and detected generated code.
- Include other hidden files unless excluded.
- Ignore parent-workspace, user-global, `.ignore`, `.rgignore`, and
  backend-specific ignore rules.
- Not follow directory symlinks.

`--include-generated` includes detected generated source but not build outputs
unless an explicit path includes them. Optional backends MUST implement this
contract instead of inheriting their own traversal defaults.

## File search

```bash
dnaxi search file <query>
```

File search MUST perform invariant ordinal case-insensitive substring matching
against normalized workspace-relative paths by default. It MUST support
`--case-sensitive`, repeated `--extension`, repeated `--glob`, `--path`,
`--project`, `--changed`, `--include-generated`, and `--limit`.

Default rows contain ID, normalized path, file kind, and owning-project count.
A path with multiple owners is represented once; ownership detail is available
through `--fields`.

No matching files produce `count: 0`, an empty `files` array, and exit `0`.

## Text search

```bash
dnaxi search text <query>
```

Literal search is the default. The command MUST support `--regex`,
case-sensitive and insensitive modes, path/project scope, changed-file scope,
and explicit generated-code inclusion.

Before compilation is needed, `--project` resolves the selected project through
workspace-selection precedence and restricts ordinary text search to that
project's directory; it does not imply evaluated project-item ownership.

The tool MAY use `rg` when available, but the built-in engine defines the
stable contract and MUST remain available as a fallback.

Default rows SHOULD contain file, line, match preview, and match ID. Results
include the total count when the scan determined it; otherwise they state that
the total is unknown because collection stopped at a limit. No matches are a
successful zero-result response with exit `0`.

### Regular expressions

`--regex` uses the .NET regular-expression language with
`RegexOptions.CultureInvariant` and a configurable per-file timeout. The
built-in engine is authoritative. `rg` MAY accelerate only cases where its
adapter proves equivalent matching, case, encoding, and line behavior.

Invalid expressions and timeouts produce structured errors identifying the
query and affected file without a stack trace.

### Encoding and case

Text search MUST support UTF-8, UTF-8 with BOM, and UTF-16 source text
recognized by .NET. Binary or undecodable files are skipped and counted, with
details available through an opt-in field; they do not fail unrelated search.

Literal case-sensitive matching uses ordinal comparison. Literal
case-insensitive matching uses invariant ordinal semantics and does not depend
on host locale.

## Structural search

General backend-specific pattern and rule commands are outside the MVP. The
MVP exposes the stable, tool-owned C# syntax queries below and implements them
with Roslyn syntax trees.

A future general structural-search surface MUST define product-owned query
semantics, traversal, provenance, cancellation, limits, and semantic-verifier
behavior. It also requires benchmark evidence that the stable syntax queries
are insufficient. Third-party pattern syntax or output schemas MUST NOT become
the public contract by accident.

Syntax-only matches MUST NOT be described as compiler-confirmed facts or
silently trigger source rewrites.

## Stable syntax queries

The MVP exposes tool-owned C# syntax queries:

```bash
dnaxi search syntax invocation --name SaveChangesAsync
dnaxi search syntax class --attribute Authorize
dnaxi search syntax object-creation --type HttpClient
dnaxi search syntax catch --type Exception --empty
```

Roslyn syntax is the authoritative MVP implementation. It parses only selected
files, follows the shared traversal contract, and does not require a
compilation or execute repository code.

### Semantic verification

A stable syntax query MAY accept `--verify` when its tool-owned query kind
declares one unambiguous compiler construct. Verification maps candidates to
every owning project and selected target framework, loads only candidate scope
when possible, resolves the construct with Roslyn, and reports discovered,
verified, rejected, and unresolved counts.

Arbitrary syntax nodes do not invent a compiler meaning. A query without a
declared verifier rejects `--verify` with an actionable structured error.

## Symbol declarations

```bash
dnaxi search symbol <query>
```

Matching SHOULD rank exact fully qualified names, exact identifiers,
case-insensitive exact matches, prefixes, camel-case/token matches, substrings,
and optionally documentation/declaration text.

The command MUST support `--kind`, `--namespace`, `--project`, `--path`,
`--accessibility`, `--include-tests`, and `--include-generated` where
applicable. It MUST identify candidate declarations before loading semantic
projects.

Default rows SHOULD include only ID, kind, name, and location. Additional
fields are requested through `--fields`.

### Stateless entity identity

Results MUST include a versioned opaque entity ID usable by later processes
without a daemon, database, or surviving cache. The ID is deterministically
derived from stable declaration identity plus enough content/location
fingerprinting to rediscover it in the associated snapshot.

After tool state is deleted, resolving an unchanged ID MUST produce the same
entity. When relevant content or configuration changed, resolution either
proves the same declaration under a new snapshot or returns
`evidence.stale_id` with replacement candidates and a concrete query. An ID
MUST NOT silently bind to another overload or declaration.

A declaration included in multiple projects or target frameworks has one
logical identity plus explicit owner/configuration variants. Queries MUST NOT
collapse variants whose compiler meaning differs.

The exact opaque ID encoding is an implementation choice as long as these
identity and stale-resolution guarantees hold.

## Show and outline

```bash
dnaxi show symbol <symbol>
dnaxi show document <path>
dnaxi outline <path-or-symbol>
```

`show symbol` returns declaration identity, signature, containing
project/type, source location, documentation preview, applicable body preview,
and cheap relationship summaries.

`show document` defaults to an outline and bounded preview instead of dumping
a large file.

`outline` returns imports, namespace, types, members, signatures, and relevant
attributes through Roslyn syntax.

## Bounded context

```bash
dnaxi context symbol <symbol> \
  --include declaration,callers,callees,tests \
  --max-chars 12000
```

The command MUST enforce an explicit or configured output budget.

The `0.4.0` slice composes declaration, owner, document, and outline evidence.
Relationship sections such as references, callers, and callees become
available with the corresponding `0.5.0` capabilities; requesting an
unavailable section returns a capability correction rather than partial
unlabeled output.

When truncated, it reports actual included size, total known size, omitted
sections, and a complete `--full` or larger-budget command. Repeated calls
against an unchanged snapshot use deterministic ordering.

Context bundles preserve source locations, symbol identities, resolution,
workspace snapshot, and relationship provenance so an agent can distinguish
tool evidence from its own inference. They SHOULD report actual character
count and an approximate token range.

The same declaration or source span SHOULD NOT be repeated through multiple
relationships. Shared evidence SHOULD be emitted once and referenced by stable
ID where practical.

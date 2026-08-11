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

## Shared output-field selection

Every command that exposes `--fields` accepts a compact comma-separated value,
for example `--fields id,file,line`. Repeated flags and the existing
space-separated multi-value form remain accepted. The CLI splits every supplied
value on commas, trims whitespace around each field name, and flattens mixed
forms in caller order before applying ordinal field validation and canonical
selection. Empty segments are blank field values and produce the command's
structured field-usage error and complete field catalog; unknown names use the
shared unknown-field error and the same catalog.

Duplicate names do not duplicate output fields. Defaults, final projection
order, and result schemas remain defined by each command's field set rather
than caller ordering or current culture. Generated examples, corrections, and
recovery commands use one canonical comma-separated `--fields` value, while
help presents that compact form first and notes the compatible repeated and
space-separated forms.

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

Invocation `--name` matching is ordinal and compares the requested value with
the terminal Roslyn identifier's value text. It therefore handles escaped
identifiers uniformly and ignores generic arity while matching simple names,
member access, and conditional member binding. A recoverable malformed
invocation is a candidate when Roslyn still exposes that terminal name. The
query does not infer aliases, extension-method binding, overload identity, or
any other compiler meaning. `--path` narrows the shared traversal scope and
`--include-generated` applies the shared generated-source policy.

Attributed-class `--attribute` matching is ordinal and compares the requested
terminal name with each class-attached attribute's terminal Roslyn identifier
value text. The optional `Attribute` suffix is removed from both names when it
follows a non-empty base name, so `Authorize` and `AuthorizeAttribute` select
the same candidates. Qualified and alias-qualified attribute syntax uses its
terminal identifier, and a target specifier on a class-attached attribute list
does not change the match. Each `ClassDeclarationSyntax` is emitted at most
once even when several attributes match; nested, static, and partial classes
remain ordinary class candidates. Records, structs, interfaces, and
compilation-unit attributes are not class candidates. Recoverable malformed
syntax remains a candidate only when Roslyn attaches both the attribute name
and the class declaration. Its reported start location is the class keyword,
not a preceding attribute list, so declaration path-and-line facts identify
the declaration line. The query does not resolve the attribute type,
aliases, target validity, or any other compiler meaning. `--path` and
`--include-generated` follow the shared traversal policy.

Object-creation `--type` matching is ordinal. Explicit object creation compares
the requested value with the constructed type's terminal Roslyn token value
text; qualification is ignored and generic arity does not affect an identifier
name. Explicit array creation applies the same rule to its element type.
Anonymous-object and implicit-array creation are not candidates.
A recoverable malformed explicit creation remains a candidate when Roslyn
still exposes the requested terminal type name.

Target-typed `new()` exposes no type name in syntax, so every such expression
is retained only as an unresolved candidate: it may construct the requested
type, but deciding that requires compiler semantics. Object-creation rows make
this distinction explicit through `type_match`, which is `exact` for an
explicit terminal-name match and `unresolved` for target-typed `new()`.
Neither value is a compiler-confirmed type identity. `--path` and
`--include-generated` follow the shared traversal policy.

Catch search without `--type` returns both typed and untyped catch clauses.
With `--type`, matching is ordinal and compares the requested value with the
catch declaration type's terminal Roslyn token value text; qualification is
ignored and generic arity does not affect an identifier name. Untyped catches
are excluded by a type filter because they expose no syntactic type name.
Catch filters do not change type matching.

`--empty` retains catch blocks with zero parsed Roslyn statements. Comments and
other trivia therefore remain empty, while an empty statement or any other
parsed statement is non-empty. A recoverable malformed catch remains a
candidate when Roslyn still exposes a catch clause that satisfies the selected
type and statement-count filters. The query does not resolve aliases,
inheritance, filter truth, or any other compiler meaning. `--path` and
`--include-generated` follow the shared traversal policy.

### Semantic verification

A stable syntax query MAY accept `--verify` when its tool-owned query kind
declares one unambiguous compiler construct. Verification maps candidates to
every owning project and selected target framework, loads only candidate scope
when possible, resolves the construct with Roslyn, and reports discovered,
verified, rejected, and unresolved counts.

Arbitrary syntax nodes do not invent a compiler meaning. A query without a
declared verifier rejects `--verify` with an actionable structured error.

The declared MVP verifiers are invocation method binding, attributed-class
attribute binding, object-creation type binding, and catch-clause type or
validity binding. Ordinary syntax search remains parse-only. `--verify` uses
the workspace-selected SDK and Roslyn's MSBuild design-time project loader; it
is explicitly classified as executing because design-time targets can run
repository code and write artifacts. It does not restore missing inputs or
silently substitute a different framework.

Frameworks and conditional metadata come from MSBuild's evaluated imports for
the effective configuration, not lexical project-file guesses. Verification
derives source ownership from each variant's evaluated `Compile` items, so
linked sources introduced by imports, properties, or globs remain in scope.
Passive directory ownership is retained only when project evaluation fails, so
that the failure remains explicit instead of disappearing from coverage.
Verification
checks that each syntax candidate still has the same content-derived identity
in the loaded document. A changed candidate is unresolved as `candidate.stale`
instead of being remapped by location. Semantic evidence uses a new snapshot
that includes the syntax snapshot, selected SDK/MSBuild runtime, evaluated
project inputs, source trees, compiler options, and metadata references.

Counts classify discovered syntax candidates, so they always partition
`discovered`. A candidate is verified when at least one owner/configuration/
framework variant verifies it, unresolved when none verifies and at least one
variant cannot be resolved, and rejected otherwise. Each candidate still
lists every attempted variant with its individual status, resolved symbol when
available, and a stable reason such as `ownership.not_found`,
`metadata.missing`, `semantic.ambiguous`, or `semantic.unresolved`.
Any unresolved variant makes result coverage and command status partial even
when another variant verifies the same candidate. Partial reasons remain
machine-readable and no unresolved variant is dropped from output.

## Symbol declarations

```bash
dnaxi search symbol <query>
```

Declaration search parses the shared eligible C# path set without creating a
compilation or evaluating a project. It includes namespaces, named types,
delegates, methods, constructors, destructors, properties, indexers, events,
fields, enum members, operators, and conversion operators. Overloads and
partial declarations remain separate candidates.

Matching ranks an ordinal exact fully qualified name, ordinal exact
identifier, case-insensitive exact name, identifier prefix, camel-case or
token match, and identifier substring in that order. Fuzzy matching applies to
the declared identifier rather than its containing type or namespace so a
type-name query does not flood results with all of that type's members. Ties
follow the shared deterministic result ordering.

The command supports repeatable `--kind` and `--accessibility` filters.
`--namespace` includes the exact namespace and descendants. `--solution` and
`--project` use shared workspace entry-point selection; solution membership is
read passively and project selection does not evaluate project items. `--path`
constrains that selected traversal and `--include-generated` uses the shared
traversal policy. Tests are excluded by default and `--include-tests` restores
declarations whose nearest effective passive project owners are all test-named
projects; a shared file with a production owner remains eligible. These
classifications and all owner variants remain syntax and path candidates, not
semantic claims.

The effective solution, de-duplicated project owners, explicit paths, and
test/generated eligibility form one structural scope descriptor shared by
declaration search and ID resolution. They participate in the workspace
snapshot even when the selected traversal is empty. Evidence reports those
effective values, so a caller can distinguish production-only evidence from a
test/generated-inclusive result without inferring policy from a command line.

Default rows include kind, name, file, and line. Candidate ID, namespace,
fully qualified name, signature, accessibility, ownership, test/generated
classification, complete range, external-path marker, and rank remain opt-in
through `--fields`. Every internal declaration result still carries its entity
ID; only the compact CLI projection requires the caller to request it.

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

The identity-hardening implementation emits `symbol/v2` identities because
compiler-variant context changes the stable fingerprint contract introduced by
the earlier `symbol/v1` slice. Each ID
contains separate opaque stable-declaration and source-location fingerprints.
The location fingerprint distinguishes byte-identical declarations in
different files. Resolution first prefers the exact complete ID; after an
otherwise unchanged source file moves, it may use the stable fingerprint only
when that identifies exactly one current candidate. Multiple candidates remain
explicitly ambiguous and are never reported as resolved. Resolution scans the
supplied workspace traversal and does not read or require retained tool state.
If neither exact nor unique stable resolution succeeds, the result is stale. It
returns `evidence.stale_id`, deterministic replacement candidates discovered
from the encoded declaration name, and a concrete full symbol-search query; it
never promotes one replacement implicitly.

The stable declaration fingerprint also covers the ordered compiler-variant
contexts supplied for the source. A variant context includes its owning
project, optional configuration and target framework, and an opaque context
fingerprint. Project, configuration, framework, or direct project-file changes
therefore make an earlier ID stale even when the declaration text is unchanged.
One logical declaration row exposes every context through opt-in
`variant_count` and `variants` fields. These passive rows carry
`meaning: unresolved`: they identify owner/configuration/framework candidates
for the source file but do not claim that the default syntax parse proves the
declaration exists or has the same meaning in each context. Semantic
verification must resolve those rows later and must not select one implicitly.

## Show and outline

```bash
dnaxi show symbol <symbol>
dnaxi show document <path>
dnaxi outline <path-or-symbol>
```

`show symbol` returns declaration identity, signature, containing
project/type, source location, documentation preview, applicable body preview,
and cheap relationship summaries.

The command accepts one canonical `symbol/v2` identity from `search symbol`
and resolves it through the same passive syntax traversal. It accepts the same
applicable `--solution`, `--project`, `--path`, `--include-tests`, and
`--include-generated` scope inputs; an ID outside that effective eligibility is
not silently resolved. `--max-chars`
defaults to 1,000 Unicode scalar values and applies independently to the
documentation and body previews. Each preview reports its included, total,
and omitted character counts; a truncated preview includes a complete command
with a sufficient larger budget. Bodyless declarations retain an explicit
empty preview. Cheap summaries are syntax-local counts for attributes,
parameters, type parameters, members, base types, and sibling overloads; they
do not claim semantic relationship evidence.

An ID discovered through an explicit symbol-search scope reuses that complete
scope with `show symbol`. This keeps external files and explicitly selected
build output available without retaining tool state. Truncation, stale, and
ambiguous recovery commands preserve the effective entry-point selector,
paths, and test/generated eligibility.

Current primary-constructor identities include their parameter signature.
Resolution also recognizes `symbol/v2` primary-constructor fingerprints emitted
before that signature enrichment, then returns the current identity and detail.

Malformed or unsupported identities are usage errors. Stale identities return
bounded replacement candidates and the existing full symbol-search correction.
An identity that resolves to multiple current declarations returns bounded
candidates and remains explicitly ambiguous.

`show document` accepts one explicit file path and returns the normalized path,
the stable `file/v1` identity also emitted by file search, passive project
ownership, external and generated markers, content-derived snapshot evidence,
encoding and byte-order-mark detail, byte size, an outline reference, and a
bounded preview instead of dumping a large file. External files are available
because the positional path is explicit and remain visibly external and
unowned. Generated files remain excluded unless `--include-generated` is
present.

The preview defaults to 1,000 Unicode scalar values. `--max-chars` selects a
different bound and `--full` explicitly returns the whole document; the two
options cannot be combined. Independent `--start-line` and `--end-line`
selectors bound the document preview by one-based inclusive lines. An omitted
start means the first line and an omitted end means the final line, so the file
and line from `search text` compose directly with `show document --start-line`
without reinterpretation. LF terminates one line and an immediately preceding
CR remains part of the same CRLF terminator. A final line without a terminator
is included, and an empty document has one empty line. Non-positive or reversed
selectors are usage errors; a selector beyond the known final line returns a
`document.line_span_out_of_range` failure with the valid line range rather than
clamping the request.

Successful results report the document `line_count`, the requested span after
resolving omitted boundaries, and the actual line span represented in the
returned preview. Character truncation can therefore make the actual end line
earlier than the requested end. A zero-character budget over non-empty selected
content has no actual start or end line. A selected line with genuinely empty
content, including the sole line of an empty document, is nevertheless reported
as the actual span when it is returned completely without truncation. The
character budget and included, total, and omitted character counts apply only
to the selected span. The default and explicitly bounded paths stream decoding,
validation, content hashing, line and scalar counting while retaining only the
requested preview and the bounded generated-code header; `--full` is the
explicit unbounded-memory path for the selected span. Truncation reports a
complete `--full` retrieval command that preserves both line selectors, and
generated-source corrections preserve them as well. Calls without selectors
continue to select the complete document. Supported text encodings are strict
UTF-8 with or without a byte-order mark and byte-order marked little- or
big-endian UTF-16. Binary, undecodable, unsupported, missing, and unreadable
documents return structured failures without leaking partial content. Before
reporting verified evidence, a second fixed-buffer pass rejects concurrent
content changes and reproduces the original `v1` raw-byte snapshot derivation
without retaining the document. Source syntax need not parse successfully for
the document text to be shown. The outline reference preserves the normalized
document path. Eligible C# documents report the capability as available and
include a concrete `dnaxi outline <path>` command; other document types keep it
unavailable. Generated-document references preserve explicit generated-source
inclusion.

`outline` returns imports, namespace, types, members, signatures, and relevant
attributes through Roslyn syntax. It accepts one explicit C# document path or
one canonical `symbol/v2` identity. Symbol targets use the same solution,
project, path, test, and generated scope as declaration search and symbol show;
document targets do not reinterpret those workspace-scope inputs. Symbol
truncation and resolution recovery commands preserve the complete effective
scope.

The output is a flat source-ordered sequence whose `depth` reconstructs syntax
nesting without duplicating parent declarations. Each item reports a stable
`syntax/v1` identity, kind, applicable name, body-free signature, declaration
attribute lists, depth, and one-based source range. Namespace children, nested
and partial types, enum members, fields, events, properties, methods, operators,
delegates, imports, global attributes, and top-level statements remain
distinguishable. Signatures omit executable bodies and initializers; property
and event accessor shape remains visible.

Document targets reuse strict document decoding, generated/external policy,
file identity, ownership, and raw-byte snapshot evidence from `show document`.
Symbol targets reuse stateless entity resolution and return only the resolved
declaration plus its syntax children, with the selected declaration at depth
zero. Stale and ambiguous IDs retain the existing bounded replacement
corrections.

Outline collection defaults to 100 items. `--limit` selects another bound and
`--full` returns every item; the two options cannot be combined. Truncation
reports the known total, omitted count, and a complete scope-preserving full
command. Recoverable malformed C# still returns available syntax structure and
a diagnostic count. Outline evidence remains syntax resolution with candidate
confidence and never requires a compilation, project evaluation, or an
optional external engine.

## Bounded context

```bash
dnaxi context symbol <symbol> \
  --include declaration,callers,callees,tests \
  --max-chars 12000
```

The command MUST enforce an explicit or configured output budget.
Budget resolution uses `--full` first, an explicit character limit second, a
configured limit third, and the built-in default last. `--full` is unbounded
and is mutually exclusive with an explicit character limit. A larger-budget
request is an explicit rerun whose limit is sufficient for the known total.

Section costs count Unicode scalar values in the representation that the
caller will emit. Sections are ordered first by their declared priority and
then by ordinal name. The budgeter includes only whole sections: an exact fit
is included, an oversized section is omitted, and later sections that fit may
still be included. This makes selection independent of input enumeration,
culture, and repeated-call order.

The `0.5.0` slice composes declaration, owner, document, and outline evidence.
Relationship sections such as references, callers, and callees become
available with the corresponding `0.6.0` capabilities; requesting an
unavailable section returns a capability correction rather than partial
unlabeled output.

When truncated, it reports actual included size, total known size, omitted
sections, and a complete `--full` or larger-budget command. Repeated calls
against an unchanged snapshot use deterministic ordering.

The total and omitted character count are present only when every section
reports a known total. An included but incomplete section is named among the
omitted sections. When a known total can satisfy a budget-only truncation, the
recovery command uses that total as a larger explicit budget; otherwise it
uses the concrete full command. Every truncated result requires one of those
recovery commands.

Context bundles preserve source locations, symbol identities, resolution,
workspace snapshot, and relationship provenance so an agent can distinguish
tool evidence from its own inference. They SHOULD report actual character
count and an approximate token range. The reusable budgeter estimates the
range as `ceil(included characters / 6)` through
`ceil(included characters / 2)`; an empty result reports zero through zero.

The same declaration or source span SHOULD NOT be repeated through multiple
relationships. Shared evidence SHOULD be emitted once and referenced by stable
ID where practical.

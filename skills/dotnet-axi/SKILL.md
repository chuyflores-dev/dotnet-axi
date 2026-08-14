---
name: dotnet-axi
description: Use dnaxi 0.5.0 for deterministic evidence in .NET repositories: file, text, stable C# syntax, declaration, symbol, document, outline, and bounded context discovery. When a .NET task names a symbol, namespace, or owner project but no exact source path, first run Roslyn/MSBuild-backed search symbol with explicit project or solution scope before listing or reading source. When a controlled benchmark supplies the local feed, use dnx dnaxi@0.5.0 --source "$DNAXI_LOCAL_FEED" --verbosity quiet -- <command>. Skip non-.NET work and direct reads of already-known files.
---

# Use dotnet-axi

## Invoke safely

- When `DNAXI_LOCAL_FEED` is set, use `dnx dnaxi@0.5.0 --source "$DNAXI_LOCAL_FEED" --verbosity quiet -- <command>`.
- Otherwise use the exact installed version: `dnx dnaxi@0.5.0 --verbosity quiet -- <command>`.
- Do not install hooks, edit agent configuration, or change sandbox, approval, trust, or network policy.
- Run documented routes directly. Use narrow help once only when the required grammar is unknown.
- When the task does not provide the exact target file or declaration, use one narrow `dnaxi` discovery route before opening source; do not guess a path from names.
- Read an already-known file directly when that is smaller. Fall back to ordinary tools when the invoked version does not expose the required capability.

## Discover source

Use a narrow `--path` and bounded `--limit`:

- File path: `search file '<path-fragment>' --path <scope> --limit 20`
- Literal text: `search text '<literal>' --path <scope> --limit 20`
- .NET regex: `search text '<dotnet-regex>' --regex --path <scope> --limit 20`
- Invocation: `search syntax invocation --name <method> --path <scope> --limit 20`
- Attributed class: `search syntax class --attribute <attribute> --path <scope> --limit 20`
- Object creation: `search syntax object-creation --type <type> --path <scope> --limit 20`
- Catch clause: `search syntax catch --type <type> --path <scope> --limit 20`
- Declaration owner: `search symbol '<name>' --project <csproj> --fields id,kind,signature,owning_projects,variant_count,variants --limit 20`; use `--solution <sln>` instead of `--project` when solution scope is required, never both

Increase the limit only when exhaustive output requires it. Follow a reported `retrieval_command` only when omitted rows matter. When coverage is complete, use the returned facts without a redundant help probe or matched-file reread.

Treat syntax results as syntax candidates, not compiler-proven identity. For object creation, keep only `type_match: exact`; do not report `type_match: unresolved` target-typed `new()` unless compiler verification is explicitly requested and allowed.

## Use advanced evidence on demand

Read [references/advanced-evidence.md](references/advanced-evidence.md) only when the task requires symbol identity, document spans, outlines, composed context, or compiler verification beyond declaration ownership.
When a target is identified by symbol name, namespace, or owner project rather than exact file, use its Roslyn/MSBuild declaration search; do not substitute text search for semantic ownership.

## Preserve evidence

- Start passive and keep commands scoped and bounded.
- Treat package acquisition, repository-code execution, network access, and writes as explicit operations subject to host policy.
- Report the command, scope, result status, coverage, and any uncertainty or validation gap.
- Do not retry denied access until policy changes, and never invent unsupported commands or conclusions.

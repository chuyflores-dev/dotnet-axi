---
name: dotnet-axi
description: Use dotnet-axi for deterministic .NET repository evidence. Trigger for finding .NET files by path, searching literal or regular-expression text, locating stable C# syntax shapes or declarations, resolving symbol identity, retrieving bounded source context, inspecting workspace, semantic, impact, or analysis evidence, and validating completion. When a controlled benchmark supplies the local feed, route applicable discovery through dnx dnaxi@0.5.0 --source "$DNAXI_LOCAL_FEED" --verbosity quiet -- <command>; skip non-.NET work and direct reads of already-known files.
---

# Use dotnet-axi

- Use this skill as an on-demand guide. Do not install hooks, edit agent configuration, include live workspace state, or change the host sandbox, approvals, trust, or network policy.
- Treat portable discovery by a harness as skill availability, not evidence that the harness is a supported setup adapter.

## Route the task

- Use dotnet-axi for a .NET workspace when deterministic structured evidence is useful.
- Use only a workspace, source, semantic, impact, analysis, or validation capability reported by the invoked version.
- Skip dotnet-axi for non-.NET work.
- Skip dotnet-axi when a direct read of an already-known file is the smaller operation.
- Skip any capability that the invoked version does not report and use an available direct tool instead.

## Start with dnx

1. For .NET file, literal, regular-expression, stable-syntax, declaration, or bounded symbol-context discovery, run the matching route through `dnx dnaxi@0.5.0 --verbosity quiet --` when the invoked version reports it.
2. Invoke known source-discovery routes directly; do not add a help probe before a known route. Inspect only the narrowest relevant help once when no documented route or option applies.
3. Read an already-known file directly when that is smaller. If the required capability is unavailable, use an available direct tool and report the gap.

## Invoke on demand

1. Default to one-shot `dnx dnaxi@0.5.0 --verbosity quiet -- <command>`. Keep the exact version pin and do not require a permanent installation.
2. When a controlled harness supplies `DNAXI_LOCAL_FEED`, keep candidate resolution source-pinned with `dnx dnaxi@0.5.0 --source "$DNAXI_LOCAL_FEED" --verbosity quiet -- <command>`.
3. Use a global `dnaxi <command>` or local `dotnet tool run dnaxi -- <command>` only when that persistent invocation was explicitly selected and verified.
4. Use `dnx dnaxi@0.5.0 --verbosity quiet --` for a passive workspace summary, `dnx dnaxi@0.5.0 --verbosity quiet -- --help` only when command grammar is unknown, and `dnx dnaxi@0.5.0 --verbosity quiet -- --version` when version identity matters.
5. Treat the invoked version's structured help, version, and reported capabilities as authoritative. Never use a command or option that it does not expose.
6. Remember that `dnx` package resolution may download or restore the tool. Keep that network operation explicit and subject to host policy.

## Follow reported capabilities

Apply this flow only when the invoked version reports the relevant capability.

- Use text search for literals.
- Use stable syntax queries for syntax shape.
- Use declaration search and resolved symbol operations for exact source identity.
- Inspect impact before public changes.
- Request bounded context.
- Run fast validation during work.
- Run standard validation before completion.

## Discover source with bounded queries

1. Use the exact routes below directly when the invoked version reports them. Do not run a redundant help command before a known file, literal, regular-expression, or stable-syntax route. If a route is unavailable, use an available direct tool and report the capability gap instead of inventing a command.
2. Find a file by normalized path with `dnx dnaxi@0.5.0 --verbosity quiet -- search file '<path-fragment>' --path <scope> --limit 20`. If the exact file is already known and a direct read is smaller, read it directly.
3. Find literal text with `dnx dnaxi@0.5.0 --verbosity quiet -- search text '<literal>' --path <scope> --limit 20`.
4. Find a .NET regular expression with `dnx dnaxi@0.5.0 --verbosity quiet -- search text '<dotnet-regex>' --regex --path <scope> --limit 20`; narrow the expression or path when a file times out.
5. Find a known C# syntax shape directly with one of these stable routes: `dnx dnaxi@0.5.0 --verbosity quiet -- search syntax invocation --name SaveChangesAsync --path <scope> --limit 20`, `dnx dnaxi@0.5.0 --verbosity quiet -- search syntax class --attribute <attribute> --path <scope> --limit 20`, `dnx dnaxi@0.5.0 --verbosity quiet -- search syntax object-creation --type <type> --path <scope> --limit 20`, or `dnx dnaxi@0.5.0 --verbosity quiet -- search syntax catch --type <type> --path <scope> --limit 20`.
6. When a request requires object-creation syntax to expose the requested type, keep only `type_match: exact`; do not report `type_match: unresolved` target-typed `new()` as a requested-type match because resolving it requires compiler semantics.
7. When a bounded result reports complete coverage, return its requested facts directly without a redundant help probe or matched-file reread.
8. Treat stable syntax results as syntax candidates, never as compiler-verified symbol or type identity.
9. Text search may use compatible `rg` acceleration. When that optional engine is absent, incompatible, or unsuitable for the query, `search text` degrades to its built-in engine with the same stable command behavior.
10. Keep discovery bounded with a narrow `--path` and `--limit`. If output is truncated, follow its `retrieval_command` only when the remaining rows are needed; otherwise use the returned path or match to issue the next narrower file, text, or syntax query instead of dumping broad source.

## Resolve symbols and compose bounded context

1. Find C# declarations with `dnx dnaxi@0.5.0 --verbosity quiet -- search symbol '<name>' --solution <solution> --fields id,kind,signature,owning_projects,variant_count,variants --limit 20`. Select `--solution` or `--project` explicitly when a repository has multiple entry points, and add `--include-tests` when the target may be test-only.
2. Treat `search symbol` rows, owner projects, and framework/configuration variants as passive declaration candidates with unresolved compiler meaning. Preserve all reported variants; do not select one implicitly or call the row compiler-verified.
3. When compiler proof of a supported syntax construct is required and repository code execution is allowed, rerun its stable syntax query with `--verify`, for example `dnx dnaxi@0.5.0 --verbosity quiet -- search syntax invocation --name SaveChangesAsync --path <scope> --verify --limit 20`. Report each construct and owner/framework variant as `verified`, `rejected`, or `unresolved`; do not generalize that proof into a different symbol claim.
4. Resolve one selected canonical `symbol/v2` identity with `dnx dnaxi@0.5.0 --verbosity quiet -- show symbol '<symbol/v2/...>' --solution <solution> --max-chars 2000`. Reuse the complete discovery scope, including project, paths, tests, and generated-source eligibility. If the ID is stale or ambiguous, follow the structured correction and bounded replacement candidates, rerun the reported symbol query when needed, and select a replacement explicitly; never silently bind it.
5. Retrieve an exact document span with `dnx dnaxi@0.5.0 --verbosity quiet -- show document '<path>' --start-line <line> --end-line <line> --max-chars 4000`. Follow its larger-budget recovery only when omitted characters matter; use `--full` only for an explicitly required complete document.
6. Inspect source structure with `dnx dnaxi@0.5.0 --verbosity quiet -- outline '<path-or-symbol>' --limit 100`. Keep symbol scope consistent, and use the reported full retrieval command only when omitted outline items matter.
7. Compose bounded symbol evidence with `dnx dnaxi@0.5.0 --verbosity quiet -- context symbol '<symbol/v2/...>' --include declaration,owner,document,outline --max-chars 12000`. Reuse the selected symbol scope. Increase the budget or use `--full` only when the omitted whole sections are required.
8. In 0.5.0, `context symbol` supports only `declaration`, `owner`, `document`, and `outline`. Treat `references`, `callers`, `callees`, `tests`, implementations, and other relationship or graph requests as unavailable capability corrections; do not invent commands, sections, or conclusions.

## Preserve evidence and safety

- Start with passive discovery and inspect command classification before allowing repository-code execution, network access, or writes.
- Treat dnx package resolution as an explicit operation that may require network access; never bypass the host policy.
- Keep evidence scoped and distinguish verified facts from candidates, uncertainty, and incomplete coverage.
- Treat denied filesystem, process, or network access as a host restriction; retry only after a confirmed policy change and do not loop.

## Complete with evidence

Do not claim completion solely because files changed. When the invoked version exposes validate, use the strongest applicable `dnx dnaxi@0.5.0 --verbosity quiet -- validate` evidence available within the requested scope. Otherwise run the strongest applicable project validation and report the evidence and any gaps.

Report the command, requested scope, result status, resolution, coverage, confidence when applicable, and any remaining blocker or validation gap.

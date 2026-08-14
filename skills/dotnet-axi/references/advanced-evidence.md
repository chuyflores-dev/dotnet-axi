# Advanced dnaxi evidence

Read this reference only for declarations, symbol identity, bounded source context, or compiler verification.

## Follow reported capabilities

Apply this flow only when the invoked version reports the relevant capability.

Treat the invoked version's structured help, version, and reported capabilities as authoritative. Never use a command or option that it does not expose.

- Use text search for literals.
- Use stable syntax queries for syntax shape.
- Use declaration search and resolved symbol operations for exact source identity.
- Request bounded context.

## Resolve symbols and compose bounded context

1. Find C# declarations with `dnx dnaxi@0.5.0 --verbosity quiet -- search symbol '<name>' --solution <solution> --fields id,kind,signature,owning_projects,variant_count,variants --limit 20`. Select `--solution` or `--project` explicitly when a repository has multiple entry points, and add `--include-tests` when the target may be test-only.
2. Treat `search symbol` rows, owner projects, and framework/configuration variants as passive declaration candidates with unresolved compiler meaning. Preserve all reported variants; do not select one implicitly or call the row compiler-verified.
3. When compiler proof of a supported syntax construct is required and repository code execution is allowed, rerun its stable syntax query with `--verify`, for example `dnx dnaxi@0.5.0 --verbosity quiet -- search syntax invocation --name SaveChangesAsync --path <scope> --verify --limit 20`. Report each construct and owner/framework variant as `verified`, `rejected`, or `unresolved`; do not generalize that proof into a different symbol claim.
4. Resolve one selected canonical `symbol/v2` identity with `dnx dnaxi@0.5.0 --verbosity quiet -- show symbol '<symbol/v2/...>' --solution <solution> --max-chars 2000`. Reuse the complete discovery scope, including project, paths, tests, and generated-source eligibility. If the ID is stale or ambiguous, follow the structured correction and bounded replacement candidates, rerun the reported symbol query when needed, and select a replacement explicitly; never silently bind it.
5. Retrieve an exact document span with `dnx dnaxi@0.5.0 --verbosity quiet -- show document '<path>' --start-line <line> --end-line <line> --max-chars 4000`. Follow its larger-budget recovery only when omitted characters matter; use `--full` only for an explicitly required complete document.
6. For a code edit, once declaration search establishes the exact small file and owner, read that file directly. Do not guess a document end line or repeat semantic discovery after the edit when build or test validation is the required evidence.
7. Inspect source structure with `dnx dnaxi@0.5.0 --verbosity quiet -- outline '<path-or-symbol>' --limit 100`. Keep symbol scope consistent, and use the reported full retrieval command only when omitted outline items matter.
8. Compose bounded symbol evidence with `dnx dnaxi@0.5.0 --verbosity quiet -- context symbol '<symbol/v2/...>' --include declaration,owner,document,outline --max-chars 12000`. Reuse the selected symbol scope. Increase the budget or use `--full` only when the omitted whole sections are required.
9. In 0.5.0, `context symbol` supports only `declaration`, `owner`, `document`, and `outline`. Treat `references`, `callers`, `callees`, `tests`, implementations, and other relationship or graph requests as unavailable capability corrections; do not invent commands, sections, or conclusions.

## Preserve evidence and safety

- Start with passive discovery and inspect command classification before allowing repository-code execution, network access, or writes.
- Treat dnx package resolution as an explicit operation that may require network access; never bypass the host policy.
- Keep evidence scoped and distinguish verified facts from candidates, uncertainty, and incomplete coverage.
- Treat denied filesystem, process, or network access as a host restriction; retry only after a confirmed policy change and do not loop.

## Complete with evidence

Do not claim completion solely because files changed. The pinned 0.5.0 command set does not include a `validate` route; run the repository's own applicable `dotnet` build or test validation and report the evidence and any gaps.

Report the command, requested scope, result status, resolution, coverage, confidence when applicable, and any remaining blocker or validation gap.

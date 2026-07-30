# Repository Guidance

## Purpose

`dotnet-axi` is an agent-first CLI for deterministic .NET discovery, analysis,
validation, and safe modification. Its installed command is `dnaxi`.

## Context routing

1. Read [REQUIREMENTS.md](REQUIREMENTS.md) for product outcomes and scope.
2. Open the repository work item linked by the GitHub issue.
3. Follow that work item's links to the relevant design references.
4. Read only the references needed for that task.

Work-item files under `docs/stories/` define delivery scope. GitHub defines
live status and coordination. Design references define accepted product and
technical behavior. Do not create ad hoc TODO, progress, memory, or backlog
files elsewhere in the repository.

## Canonical verification

Use the SDK selected by `global.json` and run:

```bash
dotnet restore dotnet-axi.slnx
dotnet build dotnet-axi.slnx --configuration Release --no-restore
dotnet test dotnet-axi.slnx --configuration Release --no-build
```

Add narrower or stronger checks as implementation introduces them. Do not add
documentation-only validation workflows.

## Working agreements

- Keep `REQUIREMENTS.md` concise; put behavioral and architectural detail in
  the appropriate document under `docs/design/`.
- Execute only atomic stories; epics are planning containers and must be split
  before implementation.
- Update the relevant design reference in the same pull request when accepted
  behavior or architecture changes.
- Do not add document status, date, or version headers. Git history is the
  document history.
- Keep work status in GitHub Issues and pull requests, not work-item or design
  references.
- Do not duplicate shared guidance in tool-specific instruction files.
- Preserve unrelated user changes and avoid destructive Git operations.
- Prefer deterministic, cross-platform behavior and machine-verifiable checks.
- Treat tests and CI as enforceable truth; use prose for intent and rationale.

## Completion

Before claiming a task is complete:

- Confirm the linked story's acceptance conditions.
- Run every applicable canonical verification command.
- Review the final diff for scope, safety, and documentation consistency.
- Report checks that could not run and why.

# Stories and Epics

Repository work items define durable delivery scope. GitHub Issues and Projects
track live status, priority, ownership, discussion, and pull requests.

## Hierarchy

- An **epic** groups a product outcome. It is a planning container, not an
  executable task.
- A **story** is the smallest tracked implementation outcome and the only unit
  assigned to an agent.

MVP epics use IDs such as `MVP-E01`. Stories use IDs such as `MVP-E01-S01`
and live beside their epic's `README.md`.

```text
docs/stories/
  MVP-E01-cli-and-output/
    README.md
    MVP-E01-S01-<short-name>.md
```

## Atomic stories

A story is ready only when it:

- delivers one observable outcome;
- has one primary contract or subsystem;
- fits in one pull request and one focused agent session;
- can be verified independently;
- states its dependencies and exclusions; and
- does not require unrelated cleanup or follow-up work to be accepted.

Split a story when any condition is not true. Epics must never be handed to an
agent as if they were stories.

## Sources of truth

Use each source for one purpose:

1. [Requirements](../../REQUIREMENTS.md) define product scope and the release
   bar.
2. [Design references](../README.md) define accepted behavior and architecture.
3. Work-item files define bounded delivery slices.
4. GitHub tracks execution state.

A work item links to design instead of repeating it. If implementation changes
accepted behavior, update the relevant design in the same pull request.

## GitHub tracking

Create one GitHub issue for each epic and each story that is ready for work.

- Epic issue title: `[MVP-E01] CLI foundation and output contract`
- Story issue title: `[MVP-E01-S01] <observable outcome>`
- Issue body: link the repository work item and add only execution-specific
  coordination.
- Epic issue: use GitHub parent/sub-issue relationships to collect its stories.

Do not put status, assignee, priority, estimates, dates, or progress checklists
in work-item files. Completed work-item files remain as implementation
reference; GitHub and Git history record their lifecycle.

## File shape

Epic files contain:

- outcome;
- scope and boundary;
- design references;
- dependencies; and
- completion conditions.

Story files will contain:

- outcome;
- design references;
- a narrow boundary;
- acceptance conditions;
- verification; and
- dependencies.

## MVP epics

| ID | Epic |
|---|---|
| `MVP-E01` | [CLI foundation and output contract](MVP-E01-cli-and-output/README.md) |
| `MVP-E02` | [Workspace discovery and snapshot identity](MVP-E02-workspace-and-snapshots/README.md) |
| `MVP-E03` | [File, text, and syntax discovery](MVP-E03-file-text-and-syntax/README.md) |
| `MVP-E04` | [Symbols, identity, and bounded context](MVP-E04-symbols-and-context/README.md) |
| `MVP-E05` | [Semantic relationships and graphs](MVP-E05-semantics-and-graphs/README.md) |
| `MVP-E06` | [Static analysis](MVP-E06-static-analysis/README.md) |
| `MVP-E07` | [Validation](MVP-E07-validation/README.md) |
| `MVP-E08` | [Structured .NET SDK execution](MVP-E08-dotnet-execution/README.md) |
| `MVP-E09` | [Agent integrations](MVP-E09-agent-integrations/README.md) |
| `MVP-E10` | [Configuration and freshness](MVP-E10-configuration-and-freshness/README.md) |
| `MVP-E11` | [Security and process safety](MVP-E11-security-and-process-safety/README.md) |
| `MVP-E12` | [Packaging and compatibility](MVP-E12-packaging-and-compatibility/README.md) |
| `MVP-E13` | [Quality gates and benchmarks](MVP-E13-quality-and-benchmarks/README.md) |

Post-MVP epics are added only when that phase is scheduled.

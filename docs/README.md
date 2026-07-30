# dotnet-axi Reference

This directory contains the accepted technical design and durable work-item
scope for `dotnet-axi`.
[REQUIREMENTS.md](../REQUIREMENTS.md) stays intentionally short: it defines
the product outcomes, scope, and release bar. The documents here define how
the product behaves.

Repository work items define bounded delivery scope. GitHub Issues track live
status and coordination. Reference documents do not contain task status,
progress logs, or document-version metadata.

## Reading map

| Concern | Reference |
|---|---|
| Epics, stories, and work-item conventions | [Stories and epics](stories/README.md) |
| Design principles, authorities, evidence model, and progressive analysis | [Foundations](design/foundations.md) |
| Repository, solution, project, framework, and worktree discovery | [Workspace](design/workspace.md) |
| File, text, structural, symbol, show, outline, and context commands | [Search and context](design/search-and-context.md) |
| Project/code graphs and compiler-semantic relationships | [Semantics and graph](design/semantics-and-graph.md) |
| Compiler analysis, analyzers, validation, and SDK execution | [Analysis and execution](design/analysis-and-execution.md) |
| Safe source and project modifications | [Modifications](design/modifications.md) |
| Claude Code, Codex, and future agent setup | [Agent integration](design/agent-integration.md) |
| TOON output, errors, exit codes, budgets, examples, and schema evolution | [Output contract](design/output-contract.md) |
| Cache freshness, configuration, security, packaging, and component boundaries | [Runtime and distribution](design/runtime-and-distribution.md) |
| Performance targets, test strategy, and agent-task evaluation | [Quality](design/quality.md) |

## How to use these documents

Start with the GitHub issue, open its linked repository work item, then read
only the design references linked by that work item. If implementation changes
accepted behavior or architecture, update the corresponding reference in the
same pull request.

Add a decision record under `docs/decisions/` only when a consequential choice
needs rationale that would not be clear from the current design. Do not create
decision records for routine implementation choices.

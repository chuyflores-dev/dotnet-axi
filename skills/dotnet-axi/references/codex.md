# Codex sandbox operation

Read this reference only when dotnet-axi is being used from Codex. The portable workflow remains in `../SKILL.md`.

## Sandbox and approvals

- Treat the sandbox as the technical boundary and approvals as the mechanism for crossing it; changing the reviewer does not expand access.
- Request only the narrow scope needed for the operation and never recommend full access as an automatic recovery.
- Keep repository instructions durable and shared; do not place user-specific permission profiles or duplicate tool workflows in AGENTS.md.

See [Codex sandboxing](https://learn.chatgpt.com/docs/sandboxing) and [agent approvals and security](https://learn.chatgpt.com/docs/agent-approvals-security).

## Writable worktree roots

- Run from the selected repository or worktree as an active writable workspace root for implementation.
- If an external worktree is not writable, request that exact root instead of redirecting build outputs into another checkout.
- Respect protected Git metadata and Git's one-mutable-branch-per-worktree rule.

See [Codex worktrees](https://learn.chatgpt.com/docs/environments/git-worktrees).

## Network and protected metadata

- Prefer passive, network-free discovery first.
- Treat restore, dnx package download, and every other networked operation as explicit; require the host policy to allow the needed destination.
- Treat protected Git or agent configuration metadata, read-only source, denied network, and denied process launch as host restrictions rather than proof that dotnet-axi is unsupported.

## Worker startup boundary

- Prefer native Codex subagents for clean-context delegation; they inherit the parent turn's sandbox and live approval overrides.
- Start standalone `codex exec` only from an owning host or automation boundary already permitted to initialize Codex and access the selected worktree; never launch it as a sandboxed child to escape the current boundary.
- Treat a denial before `thread.started` as no observed Codex thread identity, not proof that the launcher process exited; preserve the diagnostic and stop polling an event stream that never began.
- Before any replacement, observe the exact launcher process and confirm exit or terminate, wait for, and reap that child; event absence or silence never authorizes a duplicate, and retry still requires a confirmed boundary change.

See [Codex subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents).

## Noninteractive workers

- Choose the sandbox explicitly: use workspace-write for implementation and read-only for review.
- Prefer ephemeral JSONL execution and capture the final response separately.
- Bound both event-stream silence and total runtime.

See [Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode).

## Bounded recovery

- Retry at most once, and only after a confirmed permission or policy change addresses the blocker.
- Otherwise stop and return the denied resource or operation, the governing host restriction, and the narrow access needed.
- Never widen access, rewrite protected metadata, redirect work to a different checkout, or enter an approval retry loop.

## Instruction boundaries

Keep tool procedure in the skill and durable repository conventions in AGENTS.md. See [Codex skills](https://learn.chatgpt.com/docs/build-skills) and [repository instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md).

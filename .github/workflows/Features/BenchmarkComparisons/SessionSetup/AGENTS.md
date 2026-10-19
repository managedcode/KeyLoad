# SessionSetup

## Purpose, entry points and ownership

Owns setup for one database/topology session under ADR-142. Follow root and workflow policies. Authenticate the same-run job, import the verified original compiled server/client/AppHost runtime and preserve original import receipts. Never restore, build, publish images, reset a database, run a workload or manufacture results here. Linux GitHub execution only; the fixture owns selected infrastructure cleanup.

## Boundaries and protected risks

Follow the [root policy](../../../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.

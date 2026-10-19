# SessionTeardown

## Purpose, entry points and ownership

Owns retention of original database/client logs and test results after the fixture joins and disposes its own Aspire infrastructure. Follow root and workflow policies and ADR-142. Aspire/TUnit owns database startup and disposal; this action performs no per-case reset, measurement, cleanup matrix or report fabrication. Preserve same-job source/runtime/process receipts, failure diagnostics, test results and least-privileged artifact upload. Never clean unrelated Docker resources.

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

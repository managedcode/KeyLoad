# Core Messaging feature

## Purpose, entry points and ownership

- Owns canonical queue, event, recurring schedule, saga, transfer and subscription behavior in the KeyLoad.Core assembly.
- Due-work discovery entry point: `Queries/DueWorkDiscovery.cs`; record interpretation stays in feature-local query helpers.
- Discovery is read-only and uses the existing ZoneTree records and prefixes. Do not add a durable index, new storage owner, public SDK operation, or alternate queue writer here.
- Return only bounded owned metadata. Never expose retained schedule payload/header JSON, saga state, or a borrowed store view.
- Apply remains in the existing atomic `DatabaseEngine` command path; one operation preserves the full atomic partition and current persisted authorization.

## Boundaries and protected risks

Follow the [root policy](../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.

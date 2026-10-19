# KeyLoad.Abstractions

## Purpose, entry points and ownership

- Owns public, implementation-independent contracts shared by KeyLoad clients and services.
- Main contract sources: `Contracts.cs`, `<SliceName>/Contracts/` (individual request/result records), `QueryExecution/Contracts/QueryAst.cs`, `ChangeFeeds/Contracts/ChangeFeeds.cs`, `EventStreams/Contracts/Subscriptions.cs`, `ResourceExecution/Contracts/Admission.cs`, `ResourceExecution/Contracts/HttpAdmission.cs`; storage contracts and key encoding are under `Storage/Contracts/StorageContracts.cs` and `Storage/Serialization/KeyCodec.cs`.
- Place feature-owned public contracts under `<SliceName>/` using the canonical name in `docs/Features/<SliceName>.md`; keep truly shared primitives here only when multiple slices require them.
- Contracts define caller-visible shapes, not persistence, authorization decisions, cluster execution, Orleans routing, or server implementation.
- Use the installed Orleans skill for runtime design and serialization decisions.
- Preserve net10.0/C# 14 and central package pins. Security, serialization, compatibility and trust-boundary changes require explicit contract review.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.

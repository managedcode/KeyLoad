# KeyLoad

## Purpose, entry points and ownership

- Owns the KeyLoad server process, HTTP API composition and Orleans silo/node configuration.
- Entry point: `Program.cs`; API routes: `ClientApi/Transport/ApiEndpoints.cs`; Orleans node setup: `OrleansHosting/Hosting/OrleansNode.Composition.cs`; configuration: `NodeOptions.cs`.
- `OrleansHosting` owns silo composition and lifetime; `RequestExecution` owns admitted grain calls and request draining; `RuntimeJournaling` owns journal startup; `StoragePartitions` owns physical partition sessions. Administrator authentication belongs to `Authorization`, and shared background storage operations belong to `BackgroundExecution`.
- Server routes public requests to database/grain behavior. New endpoint behavior belongs under `<SliceName>/` and matching `docs/Features/<SliceName>.md`; keep the process entry point as composition.
- Microsoft Orleans does all clustering (membership, discovery, failure detection, directory, placement, activation migration, communication); the server hosts the silo and adds none of its own. Public API → one grain per connected client → grains call the silo-local ZoneTree owner (node-local `PartitionHost`/Grain Service, which owns storage, journals, file locks and the apply gate) on their own or another node through Orleans, gather and return. See [ADR-136](../../docs/ADR/ADR-136-orleans-partition-ownership.md).
- Apply the installed Orleans skill at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md` to silo startup, provider configuration, directory, activation movement and shutdown.
- Authentication, authorization, endpoint limits, cancellation and health/readiness are protected. Never log secrets or claim deployment/production qualification from a build alone.

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

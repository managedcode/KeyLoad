# TimeSeries chunk work

## Purpose, entry points and ownership

Read root and KeyLoad.Clustering policy, TimeSeries requirements and ADR124 first. Contracts freeze typed bounded admission, Execution borrows signed CQRS requests and Grains own recovered Journaling/DurableJobs scheduling. `Grains/SampleChunkWorkerGrain.cs` buffers hints and flushes on a typed-option Orleans timer; `Grains/SampleChunkCoordinatorGrain.cs` owns partition-specific scheduling. Leadership never gates worker execution. The silo-local ZoneTree Grain Service owns files and engine lifetime. Every effect uses a fresh signed operation and fresh persisted creator authorization. Hint/job metadata has no rights. Persist the bounded admission before scheduling; ambiguous original scheduling remains charged across restart. Do not retry it or fabricate job identities. Preserve RuntimeJournal OrleansBinary provider, current barriers, cancellation and joined shutdown. Source/census never establishes Linux process/cluster/performance qualification.

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

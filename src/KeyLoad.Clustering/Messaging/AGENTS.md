# Orleans Messaging feature

## Purpose, entry points and ownership

- Owns the Orleans background workers for due and recurring queue work and remote queue transfers, and their typed internal contracts.
- Entry points: `Grains/RecurringDueWorkerGrain.cs` buffers events and scans persisted due work; `Grains/RecurringDueCoordinatorGrain.cs` owns partition-specific scheduling. `GrainServices/` contains the queue and transfer cycle helpers used by the worker.
- Background work uses Orleans `[StatelessWorker]` grains: each worker accepts events, buffers them and flushes the batch on a typed-option Orleans timer through the normal grain → ZoneTree owner path as a fresh signed operation, draining the existing Communication CQRS stream. Leadership never gates worker execution.
- Issue writes through the authorized connection-owned CQRS path. Do not create a scheduler principal, trust roles in hints, or move node-local storage handles with activations.
- Preserve full partition identity, persisted creator revalidation, per-operation isolation, one five-second dispatch deadline, and stable command identity across the single uncertainty retry.

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

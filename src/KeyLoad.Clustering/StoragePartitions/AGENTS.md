# StorageRecovery publication contract

## Purpose, entry points and ownership

- Owns only the shared physical-storage publication contract for ADR-136.
- The implementation is the same silo-local `PartitionHostApplicationOwner` in KeyLoad; this contract never creates a store or another owner.
- Isolated storage opens during Grain Service initialization. Shared storage opens only for the actual ordinary database activation through `ILocalDatabaseRuntime`; other silos forward through Orleans without opening the files.
- Shared activation failure and deactivation join the exact acquired physical session before its handles close. Scoped services borrow that session; they never resolve an unrelated current host or carry a handle across RPC.
- Other services observe `Opening` or await `WaitForOpenAsync` without initiating acquisition.
- Caller cancellation cancels its wait, not the original physical opening. A null `Opening` means initialization never started.
- Keep file handles, storage recovery, locks and joined close in the node-local owner; Orleans activation placement does not move them.
- Follow root and Clustering policy. Main owns coherent solution integration/build/TUnit/Aspire cluster/Linux evidence; source preparation does not qualify execution.

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

# KeyLoad.RecoveryTests

## Purpose, entry points and ownership

- Owns real-process recovery of canonical ZoneTree WAL/files, persisted command outcomes, projections, subscriptions and derived indexes.
- Main suites include `StorageRecovery/Cases/StorageRecoveryProcessTests.cs`, `StorageRecovery/Cases/StoragePublicationRecoveryTests.cs`, `DocumentStorage/Cases/CommandIdempotencyProcessRecoveryTests.cs`, `EventStreams/Cases/SharedDomainProcessRecoveryTests.cs`, `EventStreams/Cases/ProjectionProcessRecoveryTests.cs` and `Messaging/Cases/Subscription/SubscriptionFilterProcessRecoveryTests.cs`.
- Feature cases belong under matching canonical `<SliceName>/` paths; real process-host and shared recovery fixture infrastructure may remain shared.
- `Program.cs` delegates ordinary arguments to the native generated Microsoft.Testing.Platform application helper. The explicit `--recovery-process` argument runs the owning fault scenario in this same test assembly; its native exit code, original markers, real abrupt termination, process readers and file-release checks remain part of the test.
- Use the real `KeyLoad.RecoveryTests` process where required. Preserve node-local ZoneTree storage ownership (files never move with activations; Orleans does all clustering, ADR-136).
- These tests establish process-recovery behavior only. They must never be presented as power-loss durability, endurance or complete production qualification; do not substitute mocks/fakes or weaken fault assertions.
- Use the installed Orleans skill for Orleans decisions and the relevant installed skill for formatting, Aspire or diagnostics. Follow the owner's explicit installation and dependency scope.
- Treat recovery ordering, atomic file publication and filesystem durability as critical contracts. Report unconfigured coverage/complexity gates honestly.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet test --project tests/KeyLoad.RecoveryTests/KeyLoad.RecoveryTests.csproj --no-build --no-restore --configuration Release --output Detailed
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.

# KeyLoad.RecoveryHost

## Purpose, entry points and ownership

- Owns the helper process used by real-process recovery qualification.
- Executable entry point: `Program.cs`; reusable pause protocol: `StorageRecovery/Fixtures/CrashFixtureValues.cs` and `StorageRecovery/Execution/Processes/RecoveryHostPause.cs`.
- This helper is test infrastructure invoked by `RecoveryTests`; it does not own production storage or declare a durability guarantee.
- Feature-specific scenarios should be colocated under `<SliceName>/` when moved, while reusable process-host infrastructure remains shared test infrastructure. Preserve the exact crash scenario contract used by the invoking recovery tests.
- Process termination is not power-loss testing. Do not label these cases as power-loss durability proof, add mocks/fakes, or weaken cleanup and recovery assertions.
- Use the installed Orleans skill for Orleans decisions and the relevant installed skill for formatting, Aspire or diagnostics. Follow the owner's explicit installation and dependency scope.
- Preserve real process boundaries and artifacts. Report exact Actions run/SHA/artifacts for qualification; local Aspire process checks remain development evidence and do not qualify power-loss or endurance.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build tests/KeyLoad.RecoveryHost/KeyLoad.RecoveryHost.csproj --no-restore --configuration Release -warnaserror
dotnet test --project tests/KeyLoad.RecoveryTests/KeyLoad.RecoveryTests.csproj --no-build --no-restore --configuration Release --output Detailed
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.

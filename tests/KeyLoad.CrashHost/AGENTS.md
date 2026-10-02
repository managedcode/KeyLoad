# KeyLoad.CrashHost

## Purpose and entry points
- Owns the helper process used by real-process recovery qualification.
- Executable entry point: `Program.cs`; reusable pause protocol: `Features/StorageRecovery/CrashFixtureValues.cs` and `CrashHostPause.cs`.

## Ownership and boundaries
- This helper is test infrastructure invoked by `RecoveryTests`; it does not own production storage or declare a durability guarantee.
- Feature-specific scenarios should be colocated under `Features/<SliceName>/` when moved, while reusable process-host infrastructure remains shared test infrastructure. Preserve the exact crash scenario contract used by the invoking recovery tests.
- Process termination is not power-loss testing. Do not label these cases as power-loss durability proof, add mocks/fakes, or weaken cleanup and recovery assertions.

## Commands and evidence
- The helper is built as part of the GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- It is invoked by `dotnet test --project tests/KeyLoad.RecoveryTests --no-build --no-restore --configuration Release` in `.github/workflows/ci.yml`; run it only through GitHub Actions qualification.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve real process boundaries and artifacts. Report exact Actions run/SHA/artifacts; do not run recovery qualifications locally.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- This project is shared real-process recovery infrastructure; it has no independently owned product slice. Recovery feature scenarios use `Features/StorageRecovery/` in their owning test project.
- `Program.cs` is the executable entry point. Storage-recovery and replica-crash scenarios live under `Features/StorageRecovery/` and `Features/ClusterReplication/`; preserve their exact process/pipe and filesystem contracts.

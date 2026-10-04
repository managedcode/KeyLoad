# KeyLoad.RecoveryTests

## Purpose and entry points
- Owns recovery, durable-log, snapshot, peer-security, projection and subscription recovery tests.
- Main suites include `RecoveryTests.cs`, `SnapshotInstallRecoveryTests.cs`, `RaftLogTests.cs`, `PeerSecurityTests.cs`, `ProjectionRecoveryTests.cs`, `InterruptedSnapshotTests.cs`, `RaftSnapshotTests.cs` and `SubscriptionRecoveryTests.cs`.

## Ownership and boundaries
- Feature cases belong under matching canonical `Features/<SliceName>/` paths; real process-host and shared recovery fixture infrastructure may remain shared.
- Use the real `KeyLoad.CrashHost` process where required. Preserve node-local storage ownership and distinguish atomic partition identity from physical placement.
- These tests establish process-recovery behavior only. They must never be presented as power-loss durability, endurance or complete production qualification; do not substitute mocks/fakes or weaken fault assertions.

## Commands and evidence
- Local and GitHub Actions caller entry, following solution restore/build: `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=recovery`. AppHost owns the native TUnit child process, dependencies, artifacts, execution and shutdown; direct test execution is not an alternative caller entry.
- Execute only in GitHub Actions. Record exact SHA, run/job URL and artifacts; every required recovery suite must run for a pass claim.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Treat recovery ordering, snapshot replacement and filesystem durability as critical contracts. Report unconfigured coverage/complexity gates honestly.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned test slices: `StorageRecovery` and `ClusterReplication`; target paths: `Features/StorageRecovery/` and `Features/ClusterReplication/`.
- `RecoveryTests.cs`, `RaftLogTests.cs`, and `SnapshotInstallRecoveryTests.cs` are current entry points; other recovery scenarios follow the matching slice.

## Owner-authorized local development verification, 2026-10-03
- The explicit owner correction in root AGENTS.md supersedes the historical GitHub-only execution restrictions above for development verification. Run the actual Aspire-owned TUnit entry locally against a freshly built source snapshot; retain actual machine, source, command and original results. The recovery runner uses the same AppHost caller without pretending its process fixtures require an RF3 topology. Local results are development evidence. Exact-source GitHub recovery/RF3/endurance and publication gates remain required, with every necessary suite executed.

# KeyLoad.RecoveryTests

## Purpose and entry points
- Owns recovery, durable-log, snapshot, peer-security, projection and subscription recovery tests.
- Main suites include `Features/StorageRecovery/Cases/RecoveryTests.cs`, `Features/ClusterReplication/Cases/ReplicaPersistenceTests.cs`, `Features/ClusterReplication/Cases/ReplicaSnapshotMetadataRecoveryTests.cs`, `Features/ClusterReplication/Cases/PeerDiscoverySecurityTests.cs`, `Features/ClusterReplication/Cases/ReplicaTermMetadataSnapshotTests.cs`, `Features/EventStreams/Cases/ProjectionRecoveryTests.cs` and `Features/ChangeFeeds/Cases/SubscriptionRecoveryTests.cs`.

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
- Owned test slices: `StorageRecovery`, `ClusterReplication` and `ClusterRouting`; target paths: `Features/StorageRecovery/`, `Features/ClusterReplication/` and `Features/ClusterRouting/`.
- `Features/ClusterRouting/` owns the genuine prior StoredOutcome compatibility cases under TokenMigrationLineage REQ/AC-MTOKEN-005/006 and ADR-017. Reuse the existing AppHost-owned, immutable-source StorageRecovery probe infrastructure; preserve all source/driver/binary receipts and bounded process cleanup. A current serializer or rewritten Unknown/null record is not prior-frame evidence.
- `Features/StorageRecovery/Cases/RecoveryTests.cs`, `Features/ClusterReplication/Cases/ReplicaPersistenceTests.cs` and `Features/ClusterReplication/Cases/ReplicaProcessRecoveryTests.cs` are current entry points; other recovery scenarios follow the matching slice.

## Owner-authorized local development verification, 2026-10-03
- The explicit owner correction in root AGENTS.md supersedes the historical GitHub-only execution restrictions above for development verification. Run the actual Aspire-owned TUnit entry locally against a freshly built source snapshot; retain actual machine, source, command and original results. The recovery runner uses the same AppHost caller without pretending its process fixtures require an RF3 topology. Local results are development evidence. Exact-source GitHub recovery/RF3/endurance and publication gates remain required, with every necessary suite executed.
## Feature slice responsibility folders
- Place every feature-owned source file under `Features/<SliceName>/<Role>/`, using populated feature-local roles such as `Cases/`, `Fixtures/`, `Assertions/`, `Models/`, `Processes/`, `Contracts/`, `Serialization/` or `Helpers/` according to the file's actual responsibility. Preserve an existing nested scenario/domain folder and add the role beneath it. Create only roles that own files.
- Keep genuinely shared test infrastructure and project composition entry points at their existing shared ownership paths; do not duplicate them into a feature.
- Structural moves preserve every source byte, namespace, type/serializer identity and test assertion. Do not change behavior, test logic or path references as part of a layout-only move; report source-path-sensitive joins to the solution integrator.

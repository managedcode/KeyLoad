# KeyLoad.Replication

## Purpose and entry points
- Owns cluster coordination, replication state machine, durable log, bootstrap and peer-security behavior.
- Main types: `ClusterCoordinator`, `DurableRaftLog`, `ReplicatedStateMachine`, `ClusterBootstrap`, and `PeerSecurity` in the correspondingly named files.

## Ownership and boundaries
- Replication feature code belongs under `Features/<SliceName>/` with matching `docs/Features/<SliceName>.md`; this project does not own Orleans activation routing or node-local physical storage.
- Orleans is the cluster foundation and routes commands. Atomic partition identity is distinct from physical placement; node-local `PartitionHost` owns storage, journals, file locks and apply gate.
- Existing consensus implementation and root-level files are migration debt under current root policy. Do not add DotNext cluster packages, weaken replica acknowledgements, or present process-kill recovery as power-loss durability.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Replication/recovery behavior is exercised through the actual Aspire-owned caller after solution restore/build: `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=recovery`, and the same caller with `--KeyLoadTests:Suite=rf3` for Docker RF3 SDK/MCP qualification. AppHost owns native test-runner execution and shutdown and, for RF3, every Docker voter and readiness dependency; direct test execution is not an alternative caller entry. Owner-authorized local runs are development evidence; exact-source Linux GitHub qualification with all required original artifacts remains mandatory.

## Skills and protected risks
- Owner-authorized applicable skill: global [Orleans](/Users/ksemenenko/.codex/skills/orleans/SKILL.md) 3.1.1; apply its state ownership and failure-model guidance. The prior bootstrap prohibition remains for other unapproved installations.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Ordering, quorum, log durability, snapshot install and peer trust are protected boundaries. Qualification requires exact GitHub Actions artifacts and topology evidence.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slices: `ClusterReplication` and `StorageRecovery`; target paths: `Features/ClusterReplication/` and `Features/StorageRecovery/`.
- `Features/ClusterReplication/Execution/ClusterCoordinator.cs`, `Features/ClusterReplication/Storage/DurableReplicaLog.cs`, `Features/ClusterReplication/Execution/ReplicaMaterializer.cs`, `Features/ClusterReplication/Execution/ClusterCoordinator.cs`, and `Features/ClusterReplication/Identity/PeerSecurity.cs` are current slice entry points.
## Feature-local responsibility folders

- Every populated `Features/<SliceName>/` source area MUST group feature-owned C# files in populated child folders by their actual responsibility (for example `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Serialization/`, `Validation/`, `Recovery/`, `Admission/`, `Lifecycle/`, `Storage/`, or `Execution/` where applicable). Do not leave a flat mix of roles or create empty placeholders. Keep each role local to its owning feature; preserve namespaces, public signatures, serialization aliases/IDs, and source bytes during structural moves. Keep genuine project composition roots and shared cross-feature building blocks outside feature slices.

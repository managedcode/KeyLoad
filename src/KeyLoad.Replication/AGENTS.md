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
- Replication/recovery behavior is exercised by `dotnet test --project tests/KeyLoad.RecoveryTests --no-build --no-restore --configuration Release` and IntegrationTests via `.github/workflows/ci.yml`; tests run only in GitHub Actions.

## Skills and protected risks
- Owner-authorized applicable skill: global [Orleans](/Users/ksemenenko/.codex/skills/orleans/SKILL.md) 3.1.1; apply its state ownership and failure-model guidance. The prior bootstrap prohibition remains for other unapproved installations.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Ordering, quorum, log durability, snapshot install and peer trust are protected boundaries. Qualification requires exact GitHub Actions artifacts and topology evidence.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slices: `ClusterReplication` and `StorageRecovery`; target paths: `Features/ClusterReplication/` and `Features/StorageRecovery/`.
- `ClusterCoordinator.cs`, `DurableRaftLog.cs`, `ReplicatedStateMachine.cs`, `ClusterBootstrap.cs`, and `PeerSecurity.cs` are current slice entry points.

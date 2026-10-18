# ClusterReplication

Status: current implementation under development verification. Full solution,
process recovery, Aspire SDK/MCP, coverage and endurance qualification remain
open in [status.json](../implementation/status.json).

Decisions: [ADR-136](../ADR/ADR-136-native-orleans-partition-ownership.md),
[ADR-143](../ADR/ADR-143-independent-silo-replica-topology.md) and
[ADR-003](../ADR/ADR-003-durability-ack-barrier.md).

## Ownership and execution

Microsoft Orleans owns membership, discovery, failure detection, the distributed
grain directory, placement, activation movement, scheduling and communication.
Each silo-local Grain Service owns that silo's ZoneTree files, WAL, locks and
ordered apply gate. Grains call those services through typed Orleans calls;
moving an activation never moves a file or open storage handle.

The public API enters one connection grain per connected client. Database grains
order canonical commands and request operations from the required silo-local
owners. Intensive reads and background batches use native stateless workers.
Every operation retains fresh persisted authorization, bounded resources,
cancellation and joined cleanup.

`KeyLoad:SiloCount` and `KeyLoad:ReplicaCount` are independent positive startup
options. Copies cannot exceed configured or available silos. There is no
artificial maximum silo count. Native membership supplies active silo addresses;
the current implementation orders their native names and selects the configured
number for a write. This selection does not rebalance existing files or recover
a lost data set onto new empty silos.

## Writes, reads and retries

A write succeeds only after **every configured copy** returns its persisted
canonical outcome. `CommitReceipt.PersistedCopies` records that actual count;
`Durability` records the local persistence barrier. `Buffered` reports zero
persisted copies. A stopped required copy blocks successful writes even when
other copies can still serve reads.

The canonical record is a native `ManagedCode.Communication.Command<DatabaseCommand>`.
The original command ID, verified subject, timestamp, input and prepared choices
are retained with the native `Result<object>` and canonical sequence in one local
atomic transaction. A retry must match the original authorized operation and
keeps its original result and sequence. A timeout after effects may have started
is an unknown outcome, resolved using that same command ID.

If one unfinished command is present on only some copies, retrying that original
command can complete the missing applications. Unrelated divergent prefixes
return `RecoveryRequired`; a successful response never hides that condition.
General restoration of an empty or lagging data copy is not qualified by this
implementation.

Reads select an available native storage owner with the highest observed
canonical cut, preferring the executing silo when cuts match. A requested minimum
token must be reached locally before reading, and current persisted credentials,
resource permissions and projection rules still apply. Observing a cut alone
does not prove equal data or a distributed snapshot.

## Requirements and acceptance

| Requirement | Observable acceptance | Current evidence boundary |
|---|---|---|
| REQ-REP-001: all copy calls use typed native Orleans Grain Services and native membership. | AC-REP-001: public SDK/MCP operations on the Aspire topology return the same authorized model data through each serving node. | `FunctionalDatabaseGateTests` owns the model flows; exact-source runtime qualification remains open. |
| REQ-REP-002: each owner confirms only after ordered atomic apply and its selected ZoneTree persistence barrier. | AC-REP-002: process interruption and cold reopen preserve acknowledged effects and their original outcomes; malformed complete records fail closed. | Real local process recovery and replicated-cluster evidence are separate gates. |
| REQ-REP-003: every configured copy acknowledges before a write succeeds. | AC-REP-003: stopping a required copy leaves earlier seeded data readable but rejects ordinary and duplicate submissions; restart permits healthy operations with exact copy counts. | `ClusterTests.AtomicBatchSurvivesSiloRestartAndMissingCopiesRejectWrites`; not yet qualified on the rewritten source. |
| REQ-REP-004: retained original commands and canonical sequence remain consistent across retries. | AC-REP-004: the same original command completes an interrupted application without duplicate effects; unrelated prefix gaps explicitly refuse further writes. | Requires actual fault injection and independent data/outcome oracles. |
| REQ-REP-005: every serving owner enforces current persisted authorization. | AC-REP-005: unauthorized and protected-field SDK/MCP reads refuse before and after restart, with unchanged records and healthy follow-up. | [Authorization](Authorization.md) owns these assertions. |
| REQ-REP-006: admission, cancellation and shutdown bound all admitted work. | AC-REP-006: saturation explicitly refuses excess work; cancellation settles retained identities, joins producers and preserves earlier acknowledged data. | Unit ownership mechanisms and real cluster fault flows remain distinct. |
| REQ-REP-007: startup validates independent positive silo/copy counts without an artificial maximum. | AC-REP-007: 1/1, 2/1, 2/2, 3/2, 5/3 and 5/5 are valid counts; zero counts and copies exceeding silos refuse before resources start. | Configuration acceptance does not qualify a topology's runtime or durability. |

## Source and verification

- `src/KeyLoad.Clustering/ClusterReplication/Grains/DatabaseCommitGrain.cs` owns ordered native copy calls.
- `src/KeyLoad.Clustering/ClusterReplication/GrainServices/` owns native addressing of silo-local storage services.
- `src/KeyLoad.Server/StorageRecovery/GrainServices/PartitionStorageGrainService.cs` owns each silo's storage lifetime.
- `build/KeyLoad.AppHost/ClusterReplication/` owns topology and fault commands.
- Integration tests use actual SDK and official MCP clients. Process tests use actual owned child processes. Neither source presence nor a focused unit pass establishes distributed recovery.

Process-kill recovery does not prove power-loss durability. Device persistence,
coverage and endurance need their own original evidence. See
[AspireOwnedFaultCommands](ClusterReplication/AspireOwnedFaultCommands.md) and
[FunctionalDevelopmentTopology](ClusterReplication/FunctionalDevelopmentTopology.md).

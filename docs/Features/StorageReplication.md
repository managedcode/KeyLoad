# StorageReplication

Status: current implementation under development verification. Full solution,
process recovery, Aspire SDK/MCP, coverage and endurance qualification remain
open in [status.json](../implementation/status.json).

Decisions: [ADR-136](../ADR/ADR-136-orleans-partition-ownership.md),
[ADR-143](../ADR/ADR-143-independent-silo-replica-topology.md),
[ADR-146](../ADR/ADR-146-group-commit.md) and
[ADR-003](../ADR/ADR-003-durability-ack-barrier.md).

## Ownership and execution

Microsoft Orleans owns membership, discovery, failure detection, the distributed
grain directory, placement, activation movement, scheduling and communication.
Each silo-local Grain Service owns that silo's ZoneTree files (with ZoneTree's
write-ahead log), locks and ordered apply gate. Grains call those services through typed Orleans calls;
moving an activation never moves a file or open storage handle.

The public API enters one ordinary request grain per admitted operation. Each
atomic partition has its own ordinary command owner and queue; metadata has a
separate owner. A queue groups already waiting commands into one native
publication. Local primary work borrows the physical session through DI; remote
primary work and exact-effect copies use the typed Grain Service. Every borrower
joins the original physical session before close. The primary prepares once;
copies atomically publish its decided changes and original stored outcomes.
Canonical prefixes and business clocks belong to their actual command scope.
Native physical timestamps identify snapshots, not command counts or comparable
cuts across copies. See [ADR-146](../ADR/ADR-146-group-commit.md) for the current
batch, dependency and interrupted-copy contracts. Every operation retains fresh
persisted authorization, cancellation and joined cleanup.

`KeyLoad:SiloCount` and `KeyLoad:ReplicaCount` are independent positive startup
options. Copies cannot exceed configured or available silos. There is no
artificial maximum silo count. membership supplies active silo addresses;
the current implementation orders their names and selects the configured
number for a write. This selection does not rebalance existing files or recover
a lost data set onto new empty silos.

## Writes, reads and retries

A write succeeds only after **every configured copy** returns its persisted
canonical outcome. `CommitReceipt.PersistedCopies` records that actual count;
`Durability` records the local persistence barrier. `Buffered` reports zero
persisted copies. A stopped required copy blocks successful writes even when
other copies can still serve reads.

Only the selected result copy returns full command outcomes over Orleans.
Other copies return the actual applied prefix's canonical sequences and
success/error-code decisions. The coordinator compares those decisions and
positions before attaching the observed copy count to the selected original
result. Missing full results or mismatched decisions leave the write outcome
unknown. Copy repair uses the same compact acknowledgement. Every copy still
persists its complete command outcome for replay; this transport shape removes
duplicate response payloads rather than any durable outcome or copy check.

The canonical record is a execution `ManagedCode.Communication.Command<DatabaseCommand>`.
The original command ID, verified subject, timestamp, input and prepared choices
are retained with the execution `Result<object>` and canonical sequence in one local
atomic transaction. A retry must match the original authorized operation and
keeps its original result and sequence. A timeout after effects may have started
is an unknown outcome, resolved using that same command ID.

If an unfinished batch is present on only some copies, the ordinary commit owner
periodically completes the exact missing effects of the last batch that the newest
copy retained. `KeyLoad:SynchronizationInterval` is a positive, validated timer
interval with a five-second default. The timer runs only for isolated storage with
several configured copies. It shares the existing command drain: background copy
completion and new canonical writes never apply concurrently, while read barriers
can continue on an advanced copy. Timer disposal and activation shutdown join the
original drain. The next write or a stable-ID retry can complete the same work
through this one copy path. Gaps larger than the last batch
and unrelated divergent prefixes return `RecoveryRequired`; a successful response
never hides that condition.
General restoration of an empty or lagging data copy is not qualified by this
implementation.

Single-partition reads select an available owner covering the partition prefix
and fresh metadata authority. Whole-database and potentially cross-partition reads
select a complete owner covering the observed vector of logical partition cuts.
Physical copies are not independent query inputs. A requested minimum token must
be reached before reading, and current persisted policy still applies at the
actual native data cut. Observing a cut alone does not prove equal data or a
distributed snapshot. Owner rosters are cached per membership version and
invalidated after an unsuccessful publication.

A request on a single configured isolated silo with only that local silo active
in Orleans membership runs beside its local storage owner directly. The published
storage session still admits and joins the request. Shared storage and multi-silo
deployments resolve the canonical owner through the commit grain.

Interleaved owner discovery adopts the already-published owner set. Completing a
batch advances that same set and fences older discoveries; a different cached
set is invalidated. A concurrent read cannot leave the next command using a
cached canonical sequence from before the acknowledged batch.

After completing a retained copy gap, metadata and data commands select a common
current physical primary. Native conflicts protect authorization and resource
changes on that primary. Each partition still owns its separate command drain,
so waiting for a copy does not order unrelated partitions. Full-copy replication
does not establish physical partition fan-out or independent write primaries.

## Requirements and acceptance

| Requirement | Observable acceptance | Current evidence boundary |
|---|---|---|
| REQ-REP-001: all copy calls use typed Orleans Grain Services and membership. | AC-REP-001: public SDK/MCP operations on the Aspire topology return the same authorized model data through each serving node. | `FunctionalDatabaseGateTests` owns the model flows; the named Linux functional qualification is recorded below, with broader qualification kept separate. |
| REQ-REP-002: each owner confirms only after ordered atomic apply and its selected ZoneTree persistence barrier. | AC-REP-002: process interruption and cold reopen preserve acknowledged effects and their original outcomes; malformed complete records fail closed. | Real local process recovery and replicated-cluster evidence are separate gates. |
| REQ-REP-003: every configured copy acknowledges before a write succeeds. | AC-REP-003: stopping a required copy leaves earlier seeded data readable but rejects ordinary and duplicate submissions; restart permits healthy operations with exact copy counts. | `ClusterTests.AtomicBatchSurvivesSiloRestartAndMissingCopiesRejectWrites`; passed in the named Linux qualification below. |
| REQ-REP-004: retained original commands and canonical sequence remain consistent across retries. | AC-REP-004: the same original command completes an interrupted application without duplicate effects; unrelated prefix gaps explicitly refuse further writes. | Requires actual fault injection and independent data/outcome oracles. |
| REQ-REP-005: every serving owner enforces current persisted authorization. | AC-REP-005: unauthorized and protected-field SDK/MCP reads refuse before and after restart, with unchanged records and healthy follow-up. | [Authorization](Authorization.md) owns these assertions. |
| REQ-REP-006: cancellation and shutdown retain ownership of admitted work through joined cleanup. | AC-REP-006: cancellation and shutdown join producers and scoped storage borrowers, preserve acknowledged data and retained outcomes, and permit healthy subsequent requests. | Unit ownership mechanisms and real cluster fault flows remain distinct. |
| REQ-REP-007: startup validates independent positive silo/copy counts without an artificial maximum. | AC-REP-007: 1/1, 2/1, 2/2, 3/2, 5/3 and 5/5 are valid counts; zero counts and copies exceeding silos refuse before resources start. | Configuration acceptance does not qualify a topology's runtime or durability. |
| REQ-REP-008: concurrent canonical commands share one process-persistent native commit per copy. | AC-REP-008: commands of one batch receive their own ordered sequences and the outcomes they would have alone. A retry inside a batch replays the first outcome without another sequence. A command that fails outside its own persisted outcome commits first in the next batch. Business time never decreases along a batch. Reset discards only the current command's writes. A copy that missed the last batch completes from its exact retained effects. | `GroupCommitTests` and `ActionStagingTests` own the engine and store contracts; the cluster batch path and its throughput are qualified by the Aspire suites and the benchmark cells. |
| REQ-REP-009: retained interrupted copy work is completed without requiring a new write. | AC-REP-009: interrupt one actual copy RPC, observe the background repair RPC, keep a read barrier available while repair is held, then verify the original document and sequence on both copies, exact retry and healthy continuation with joined cleanup. | `CopySynchronizationTests.InterruptedCopyCompletesWithoutAnotherWriteAndReadBarriersContinueDuringRepair`; local and delivered-source qualification remain separate. |
| REQ-REP-010: one primary prepares at a cut covering both partition data and metadata authority. | AC-REP-010: a coordinator beside that primary borrows through DI without a storage RPC; a remote coordinator sends one typed service call. Every copy retains exact data, revision and outcome; replay and healthy follow-up succeed. A lagging or foreign owner cannot replace a required current owner. | `StoragePreparationLocalityTests` separates owner-selection checks from a real two-host command, replay and follow-up. Aspire throughput and delivered-source qualification remain separate. |
| REQ-REP-011: secondary copy responses omit duplicated outcome payloads while retaining the original copy-agreement checks. | AC-REP-011: a real two-host batch returns one full outcome and one compact acknowledgement with the same persisted decision; exact bodies, revisions, replay and healthy continuation hold. Wrong sequence, decision, prefix or a missing selected outcome return `UnknownWriteOutcome` after the real commit, then an exact retry resolves it without duplicate effects. | `StorageBatchAcknowledgementTests` and `StorageBatchAcknowledgementFailureTests`; local, delivered-source and throughput qualification remain separate. |

## Source and verification

Runtime revision [`b55245893`](https://github.com/managedcode/KeyLoad/commit/b55245893c93409e41d7ca31edfec967da173045) passed all 33 jobs of [Linux Build and Tests](https://github.com/managedcode/KeyLoad/actions/runs/42122435945), including the compiled inventories, functional operations, process recovery, Aspire lanes and final coverage. Its [Release](https://github.com/managedcode/KeyLoad/actions/runs/42127683554) succeeded. The original one-copy and three-copy documents cells of [Benchmarks run 42128099998](https://github.com/managedcode/KeyLoad/actions/runs/42128099998) passed all six cases each on the unchanged indexed dataset. This does not establish the requested 30,000 documents per second, partitioned concurrent ingestion, endurance or power-loss durability.

- `src/KeyLoad.Clustering/StorageReplication/Grains/PartitionCommitGrain.cs` owns each partition queue; `DatabaseCommitGrain.cs` owns metadata and shared-file acquisition. `StorageReplication/Execution/StorageCommitQueue.cs` joins foreground batches and timer-driven copy completion per owner.
- `src/KeyLoad.Clustering/StorageReplication/GrainServices/` owns addressing of silo-local storage services.
- `src/KeyLoad/StoragePartitions/GrainServices/PartitionStorageGrainService.cs` owns each silo's storage lifetime.
- `build/KeyLoad.AppHost/TestInfrastructure/` owns topology and fault commands.
- Integration tests use actual SDK and official MCP clients. Process tests use actual owned child processes. Neither source presence nor a focused unit pass establishes distributed recovery.

Process-kill recovery does not prove power-loss durability. Device persistence,
coverage and endurance need their own original evidence. See
[AspireOwnedFaultCommands](StorageReplication/AspireOwnedFaultCommands.md) and
[FunctionalDevelopmentTopology](StorageReplication/FunctionalDevelopmentTopology.md).

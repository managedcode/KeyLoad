# StorageRecovery

Storage mode selects physical ownership. With `IsolatedVolumes`, a silo-local Orleans Grain Service owns that node's ZoneTree stores, atomic WAL/journals, file locks and ordered apply gate; copies use typed Orleans calls to those owners. With `SharedVolume`, all silos use the configured shared paths and one ordinary database grain activation acquires the physical session; other silos call that owner. Shared storage requires `ReplicaCount=1` and never permits concurrent writers to the same ZoneTree files. The local runtime retains handles until actual borrowers join and the session closes. Orleans owns placement and activation movement; deployed shared-filesystem locking and flush behavior remain unqualified.

This feature specifies the canonical key codec, physical ownership and lifetime, journal/checkpoint recovery, scoped read cuts, the current format and process-kill recovery qualification. With isolated storage, a successful persisted receipt requires every configured copy to confirm durable local apply and reports the actual `PersistedCopies` ([ADR-136](../ADR/ADR-136-orleans-partition-ownership.md), [ADR-143](../ADR/ADR-143-independent-silo-replica-topology.md)); storage mode adds no format here.

Detailed contracts by topic:

- Format and identity: [CurrentFormat](StorageRecovery/CurrentFormat.md), [IdentityAuthorityAdmission](StorageRecovery/IdentityAuthorityAdmission.md), [DispatchIdentityPublication](StorageRecovery/DispatchIdentityPublication.md), [MetadataRegularFileReader](StorageRecovery/MetadataRegularFileReader.md), [OwnerLockRegularFile](StorageRecovery/OwnerLockRegularFile.md).
- Journal and checkpoint: [WriteThroughJournal](StorageRecovery/WriteThroughJournal.md), [JournalOriginalFailure](StorageRecovery/JournalOriginalFailure.md), [CheckpointOriginalCause](StorageRecovery/CheckpointOriginalCause.md), [SeededRedoCorruption](StorageRecovery/SeededRedoCorruption.md), [RecoveryFaultRootRetention](StorageRecovery/RecoveryFaultRootRetention.md).
- Read cuts: [RuntimeReadCuts](StorageRecovery/RuntimeReadCuts.md), [TransactionalReadViewOperation](StorageRecovery/TransactionalReadViewOperation.md), [CapturedReadSelectedCancellation](StorageRecovery/CapturedReadSelectedCancellation.md), [LiveRangeSelectedCancellation](StorageRecovery/LiveRangeSelectedCancellation.md), [ValueHeaderRefusal](StorageRecovery/ValueHeaderRefusal.md).

REQ-STORAGE-HOST-COMPOSITION-001 / AC-STORAGE-HOST-COMPOSITION-001: PartitionDatabaseComposition owns validation of the original host constructor inputs. PartitionHost invokes it at the same point before directory/storage acquisition, retaining the exact null checks and NodeOptions.Validate order. Ordered owner construction, rollback, materializer/verifier joins, store-close gate and all original failures remain unchanged. Existing host lifecycle, recovery and Aspire cluster whole operations plus the unchanged type200/function64 build gates verify this responsibility move; it introduces no new lifetime, operational policy or storage contract.

## Key codec and captured envelopes

The [ADR-005 validation contract](../ADR/ADR-005-canonical-keyspace-codec.md#v1-validation-and-ownership) preserves valid durable key bytes and defines explicit normalization, arity and safe malformed-input errors. The four criteria below map to the exact KeyCodec cases and to the owning codec/storage source. A failure elsewhere in a suite cannot erase an independently verified mapped KeyCodec outcome, and the Docker/Aspire replicated-cluster, endurance and power-loss gates stay separately mandatory.

The AC-KEYCODEC-002/003 rejection flows use independent literal malformed vectors (NaN and infinity doubles, unterminated text and binary, an invalid decimal digit, a 30-digit persisted decimal). Each asserts the exact typed safe error, unchanged input bytes and an exact valid composite roundtrip after the rejection. The final decimal representability guard is a defensive path implied by the canonical coefficient/exponent/scale checks; no private hook forces it.

| Requirement | Measurable acceptance and owned TUnit mapping |
|---|---|
| REQ-KEYCODEC-001: preserve ordered v1 bytes and explicit type normalization | AC-KEYCODEC-001: original goldens and10,000 seeded decimals stay exact; an independent10,000 mixed-type/composite corpus roundtrips to the defined normalized values and sorts by a semantic oracle; negatives, valid Unicode/escaping, all type tags and extrema pass. Fractional decimal literal vectors retain their bytes and decode exactly under InvariantCulture and a custom CurrentCulture with a non-ASCII negative sign. New KeyCodecMixedCorpus/KeyCodecMixedCorpusTests, KeyCodecGoldenContractTests and KeyCodecCultureContractTests under UnitTests/StorageRecovery. |
| REQ-KEYCODEC-002: corrupted input fails through safe typed errors | AC-KEYCODEC-002: literal invalid/truncated UTF-8, escapes, scalars, bool/tags, timestamp extrema violations, decimal overflow/rounding/underflow and noncanonical numeric forms return exact Corruption; empty/unknown versions remain FormatUnsupported. New KeyCodecMalformedContractTests; no payload values in messages. |
| REQ-KEYCODEC-003: writer and reader share finite component bounds | AC-KEYCODEC-003:0,1 and256 components encode/decode exactly;257 writer input fails ResourceExhausted before encoding an unsupported component and257 persisted components fail Corruption before decoding the final malformed component. Invalid UTF-16/nonfinite input gives Validation and unsupported CLR types UnsupportedCapability. New KeyCodecInputContractTests. |
| REQ-KEYCODEC-004: captured envelopes cannot alias canonical storage to caller-owned buffers | AC-KEYCODEC-004: actual ZoneTree staging copies input key/value before mutation/pool reuse; mutating a returned independently owned KeyValueRecord does not change committed bytes; generated existing envelope roundtrip and real reopen preserve the exact original content. New KeyCodecEnvelopeOwnershipTests, existing permanent aliases/Ids, real provider only. |

Canonical map: shared codec/storage carriers in `Abstractions/Storage`, cohesive validation helpers in `Abstractions/StorageRecovery`, provider ownership in `Storage.ZoneTree/StorageRecovery`, and matching real-provider tests in `UnitTests/StorageRecovery`. Apply, WAL authority and public JSON stay unchanged. UI/SDK/MCP schema work is N/A: no new wire operation.

## Exclusive node-local storage ownership

REQ-STORAGE-008 includes the exclusive physical-owner requirement. AC-STORAGE-OWNER-001 makes it measurable: while a parent owns a real current-format ZoneTreeStore, an actual compiled RecoveryTests existing-store inspection in a separate process fails with the existing safe IOException receipt. The child and both original output readers must settle before inspection returns. Identity bytes, committed value and position remain exact, and the parent can commit and read a subsequent value. A second child is still denied while that parent is open. After actual parent disposal, child inspection and ordinary parent reopen succeed with the exact identity, updated value and position. Readable canonical journal bytes are retained across denied opens; reads are not forced through the active exclusive lock handle and asynchronously maintained physical tree files are not required to stay unchanged.

Governed by [ADR-046](../ADR/ADR-046-storage-private-owners.md). The flow is `tests/KeyLoad.UnitTests/StorageRecovery/Cases/StorageOwner/StorageOwnerProcessTests.cs`, which reuses the existing ZoneTreeExistingStoreFixture, inspector process and safe-receipt assertions and keeps the real 30-second child deadline, 8-KiB output bounds, strict protocol, outer node-owner lock and original exit/readers cleanup. It adds no fake parent rejection, child variant, production hook, swallowed failure or deadline increase. The lease, maintainer and pool requirements of REQ-STORAGE-008 are specified below; the broad AC-SQ-001..008 group is not satisfied by this criterion alone.

## Current-format storage and restore contract

The single supported format, current restore authority, strict rejection
rules and their owning REQ/AC mappings are defined by
[CurrentFormat](StorageRecovery/CurrentFormat.md), [ADR-011](../ADR/ADR-011-current-format.md)
and [ADR-116](../ADR/ADR-116-first-release-current-format.md). Ordinary open,
recovery, checkpoint installation, backup and restore accept only that current
contract. Unknown or corrupt mandatory data fails closed before publication;
there is no alternate reader, conversion path or format fallback. Current-format
backup restoration creates a new authority incarnation and completes its bounded
blob-authority normalization before writable service. The source backup remains
unchanged and an incomplete restore stays paused.

The current-format positive and rejection operations map to the existing real
ZoneTree, RecoveryProcess, RecoveryTests and Docker/Aspire SDK/MCP cases named in those
owning feature contracts. Preserve exact persisted bytes, authorization, node-local
ownership and complete process/resource settlement. Source review and development
receipts do not close the required Linux or replicated-cluster qualification gates.

## Retired checkpoint handle ownership

REQ-STORAGE-020 / AC-DBHP-009 preserves one physical owner for each successfully retired maintainer/tree ([ADR-046](../ADR/ADR-046-storage-private-owners.md)). The failed-install scenario retains exact RecoveryRequired/warm rejection at InstallPrepared and JournalSwapped, then closes the log/store and repeats actual Dispose without redisposing retired handles. New healthy handles and directory locks keep their original lifetime. No new interface, package, format, caught cleanup error or relaxed fault oracle is permitted.

```mermaid
flowchart LR
    Prepare[Generation under actual write gate] --> Close[Close old maintainer then tree]
    Close --> Release[Clear successfully retired ownership]
    Release --> Swap[Existing install and journal swap]
    Swap --> Healthy[Own replacement tree and maintainer]
    Swap --> Fault[Existing recovery-required poison]
    Fault --> Dispose[Close only still owned handles]
```

## Guarded original-store inspection

REQ-STORAGE-015 maps AC-SG009-001..004 ([ADR-059](../ADR/ADR-059-isolated-intensive-timeseries.md)). The private original-store guard rejects missing original files or changed identity before provider recovery, uses provider Open rather than OpenOrCreate, and preserves ordinary public open/codecs. Real file TUnit verifies identity/data/position, missing/mismatch/locked/invalid flows and normal reopen. Independent registered cleanup retains primary and cleanup failures. Every proof uses a separately owned original inspector process and joins its exit and readers before restart, because provider partial open cannot prove handle settlement. Parent control/ACK/copy is outside this guard, and no power-loss or immutability claim is made. Ordered ownership, tests and exceptions are in ADR-059.

```mermaid
flowchart LR
    Lock[Original stopped owner] --> Identity[Existing identity and journal]
    Identity --> Execution[Existing metadata and Open]
    Execution --> Read[Permitted recovery and actual read]
    Read --> Join[Cleanup and original process settlement]
    Join --> Restart[Original restart]
```

## Bounded real crash-trial qualification

REQ-STORAGE-014 maps AC-RC-001..004 ([ADR-035](../ADR/ADR-035-memory-performance.md)). A shared test-only four-slot admission bounds each storage RecoveryProcess launch through real reopen, assertions and cleanup. It starts before each unchanged 15 s/20 s trial CTS, preserves 20x50 seeded crashes and all checkpoint/projection/subscription cases. Success receipt rows follow the atomic/durable assertions and add measured occupancy/peak fields. No fault point, assertion, readiness or job bound is reduced. The Linux GitHub recovery suite and 1000 successful seeded rows in that run qualify the harness; source and lifetime review explicitly cover rare exceptional permit/cleanup paths without doubles. Marker, receipt and input-read cancellations do not identify a storage defect.

```mermaid
flowchart LR
    Queue[Test cancellation aware admission] --> Pool[Four storage trial slots]
    Pool --> Deadline[Original trial deadline]
    Deadline --> Child[Real recovery process and kill]
    Child --> Reopen[Actual files and atomic recovery assertions]
    Reopen --> Receipt[Success receipt with observed occupancy]
    Receipt --> Cleanup[Cleanup then release owned slot]
```

REQ-STORAGE-014 / AC-RC-003 (also AC-AISQL-014): the unchanged receipt is written inside the original trial `try` immediately after the atomic/durable assertions, matching the receipt-before-cleanup order above. The original 15 s linked token, all 20x50 faults/trials, actual four-slot ownership and unconditional cleanup stay; the existing catch retains seed/trial/stage for receipt failures and any cleanup failure still fails qualification. This is test-harness ordering only: ADR-035 lifetime/test contracts suffice and no product, wire, persistence or topology change applies.

## Descending bounded borrowed ranges

REQ-STORAGE-013 / AC-RANGE-REV-001..003 ([ADR-052](../ADR/ADR-052-timeseries-bounded-aggregates.md)) accept the additive VisitReverseRange contract with the same exclusive bounds and borrowed lifetime as VisitRange. Private cursor, bounds and merge behavior are verified by real-store ReverseRangeTests, ReverseTransactionRangeTests and ReverseRangeResourceTests; interface and facade/budget forwarding stay unchanged. prefix-bounded NoRefresh reverse seek and SortedSet reverse enumeration preserve staged tombstones/replacements without complete buffering. Exact observer/lookahead/cancel/early-stop and healthy-following-operation tests are mandatory, with all existing forward tests preserved. seek failure disposal is covered by source/lifetime review, not by an executed fake fault case.

```mermaid
flowchart LR
    Gate[Original node local storage gate] --> Baseline[descending prefix bounded iterator]
    Gate --> Staged[descending bounded staged enumerator]
    Baseline --> Merge[Same canonical overlay merge]
    Staged --> Merge
    Merge --> Charge[Charge examined bytes before callback or lookahead]
    Charge --> Visitor[Borrowed bounded visitor]
```

## Killed-child file readiness

REQ-STORAGE-012 / AC-STORAGE-012 keep the killed-child filesystem readiness bound. After actual process exit, exclusive readiness covers `owner.lock`, `commands.wal` and the actual ZoneTree `tree/0.meta.wal` when present. The five-second bound, 25 ms poll, all 50 seeded trials, original trial deadlines, WAL/snapshot/atomic assertions and permanent sharing failure are unchanged. Cancellation stops before another probe; there is no retry of the recovery operation, default timeout increase or filesystem bypass.

During checkpoint installation the disposed live tree is moved aside before the InstallPrepared and JournalSwapped callbacks, so a child can exit with no tree directory. Only FileNotFoundException and DirectoryNotFoundException while opening the optional metadata WAL mean that file has no holder. `owner.lock` and `commands.wal` remain required exclusive probes; sharing violations and all other I/O errors keep the original bound. No readiness probe creates files or directories. Real missing-directory and missing-metadata-file cases assert successful readiness without recreating either path, then restore the actual store files and prove subsequent reopen and commit. Missing required ownership/journal files must still fail, and the held-file, cancel, pre-cancel and permanent-lock cases remain.

AC-REC-FUP-003 maps the two arguments of `AcStorage012_MissingRequiredOwnershipFileStillFails`. Each catches the expected exact FileNotFoundException, asserts no file creation and keeps the five-second lower / six-second upper observation assertions with 25 ms polling. Only that method carries a method-level keyless TUnit `NotInParallel`, so it does not overlap the concurrent suite; permanent-holder timing isolation and every other case stay. A timing failure, broader serialization or weakened assertion fails the criterion. No helper, timeout or retry change is allowed.

The synchronous probe is a shared internal test helper. New real-file tests hold the metadata WAL exclusively after an actual ZoneTree store closes: the barrier must remain pending, complete only after the holder is released, reject cancellation, and fail under the same bounded permanent lock. The shared entry wrapper in RecoveryTests is retained. Product APIs, formats and permissions, frontend and dependency release are N/A; ADR-035 and ADR-041 owner/lifetime contracts suffice.

The byte readback assertions in FrameBudgetTests and PreparedTransactionTests map additionally to AC-CQ-018. Pinned TUnit array equality is reference equality, so ordered content equivalence must retain every exact byte, null failure, rejected-commit and reopen obligation.

REQ-STORAGE-010 maps AC-CQ-015 and AC-SQ-002/003/004/006/007 to the preserving storage test-source stage: the references keep the named implementation and source constraints but do not establish provider behavior or any behavioral AC-SQ criterion. REQ-STORAGE-008 owns the functional ZoneTree provider contract, including AC-SQ-008. Real frame, checkpoint, scoped-read, partition-host, lock and reopen assertions stay complete; deterministic bytes, awaited equivalent file APIs and cancellation completion satisfy the enabled policy. The journal repair retains its real synchronous `Flush(true)` barrier through an awaited truncate/flush/dispose. Explicit IAtomicStore dispatch and view identity remain tested. The exact scope and audit are in the [CodeQuality](CodeQuality.md) contract; ADR-033 and ADR-032 suffice for this test-source-only refinement.

## Requirements and acceptance map

Node-local journals, owned values, committed read cuts, checkpoint/recovery and bounded provider work. [ADR-035](../ADR/ADR-035-memory-performance.md) defines the scoped-read contract; ADR-032 records existing layout migration debt.

| Requirement | Acceptance | Tests / owner |
|---|---|---|
| REQ-STORAGE-001: budget before returned page copies; spans never escape the gate | AC-MP-002 | Real ZoneTree scoped point/range byte and lifetime cases |
| REQ-STORAGE-002: exact prefix/exclusive-bound/order/overlay semantics without global overfetch | AC-MP-002 | Real transaction replacement/deletion/insert/out-of-range cases |
| REQ-STORAGE-003: cancellation/early stop release iterator and preserve canonical data | AC-MP-002/012 | Real store cancel/visitor failure, subsequent read/commit and recovery CI |
| REQ-STORAGE-004: the silo-local ZoneTree owner owns the node's stores, locks, ordered apply and shutdown | AC-ROUTE-002 | RecoveryProcess/Recovery and Docker replicated-cluster reopen, failover and activation-migration scenarios under [ADR-036](../ADR/ADR-036-orleans-foundation.md) and [ADR-136](../ADR/ADR-136-orleans-partition-ownership.md) |
| REQ-STORAGE-008: cohesive private provider owners meet numeric gates without changing storage/caller contracts | AC-SQ-001..008 | [ADR-046](../ADR/ADR-046-storage-private-owners.md); real lifetime tests; exact-source runtime qualification is a separate gate |
| REQ-STORAGE-009: validation and apply share one private mutation projection per staged generation | AC-PSW-001..004, AC-MP-006/012 | [ADR-035](../ADR/ADR-035-memory-performance.md); PreparedTransactionTests plus existing FrameBudget, recovery and replicated-cluster proof |
| REQ-STORAGE-011: startup identity metadata is finite and failed read releases physical ownership | AC-BSM-001/003/005 | [ADR-048](../ADR/ADR-048-bounded-storage-metadata.md), Metadata* real-file constructor/restore/reopen checks under BackupRestore |
| REQ-STORAGE-012: killed-child readiness includes the real metadata WAL when present, permits only precise sanctioned metadata absence, and preserves cancellation and the original failure bound | AC-STORAGE-012 | `tests/KeyLoad.RecoveryTests/StorageRecovery/Cases/KilledProcessFileReadinessTests.cs`: actual closed-store pending/release, cancellation, pre-cancellation, permanent-lock, missing optional directory/file without creation and missing required file cases; `RecoveryTests.cs` retains every process-kill scenario |

REQ-STORAGE-009 has the following current acceptance criteria. These are storage-operation contracts; local cases alone do not close process-recovery or cluster qualification.

| Acceptance | Observable contract and existing cases |
|---|---|
| AC-PSW-001 | Preparation keeps one private mutation projection per staged generation. Validation does not publish records, append a WAL frame or advance the committed position; apply consumes the final validated projection within the original atomic commit. `PreparedTransactionTests` and the existing frame-budget cases retain their exact frame and position assertions. |
| AC-PSW-002 | Repeated validation, replacement, deletion and reset preserve the final ordered mutation set, empty-value/tombstone distinction and copied caller buffers. A preparation fork reads its parent's staged image; resetting the child restores that inherited image without changing the parent. `AcPsw002RepeatedValidationReplacementDeletionResetAndCallerBuffersKeepFinalFrame` and `ForkSeesParentStageAndResetRestoresInheritedImage` exercise these contracts. |
| AC-PSW-003 | Rejected or discarded preparation preserves the original failure, committed records, position and journal. An oversized stage leaves the last valid staged set usable; reset to an empty set writes no frame. Escaped or foreign-thread preparation is refused before its callback runs. `AcPsw003RejectedOversizedStageRetainsValidatedSetForCommit`, `AcPsw003ResetToEmptyWritesNoFrameAndDoesNotAdvancePosition`, `DiscardedPreparationNeverPublishesAndPreservesOriginalFailure` and `PreparationEscapesAndForeignThreadsRefuseBeforeCallback` retain their exact conservation and healthy-follow-up assertions. |
| AC-PSW-004 | preparation and transaction views belong to their synchronous callback and original thread. Entering store preparation while the caller already holds its read or write gate is refused before the nested callback and before publication. `PreparationEscapesAndForeignThreadsRefuseBeforeCallback` and `NestedStorePreparationRefusesOwnedReadAndWriteGatesWithoutPublication` compare the complete literal image, journal and position and then perform a healthy real commit. Existing tombstone and reopen cases preserve exact bytes. |

Ownership: common public storage contracts stay in Abstractions/Storage; provider helpers in Storage.ZoneTree/StorageRecovery, with tests mirroring that name in UnitTests/RecoveryTests. The local physical session composes the store and its journal/apply lifetimes under [ADR-036](../ADR/ADR-036-orleans-foundation.md) and [ADR-136](../ADR/ADR-136-orleans-partition-ownership.md). Isolated storage binds that session to the silo-local Grain Service; shared storage binds it to the ordinary owner's activation lifetime. Logical callers receive bounded operations, never transferred file handles. ZoneTreeStore remains the public composition facade; private partial behavior lives under StorageRecovery and BackupRestore according to [ADR-046](../ADR/ADR-046-storage-private-owners.md), which accepts one runtime plus real StorageRecovery initialization, journal, view/transaction/range and checkpoint owners (BackupRestore owns the separate local backup component). No schema, UI, public, format or business-authorization change applies, and no numeric exception is authorized.

The prepared-write contract removes the duplicate mutation projection. Only transaction-private prepared arrays are cached; accepted Stage/Reset invalidate them, rejected admission preserves the prior valid set, and Stage retains caller-array ownership copies. WAL/apply/fault order and bytes remain exact.

```mermaid
flowchart LR
    Cut[Store read or commit gate] --> Iterator[Scoped ordered iterator]
    Iterator --> Bounds[Prefix and exclusive range bounds]
    Bounds --> Budget[Cancellation and bytes before copying]
    Budget --> Visitor[Read only span visitor]
    Visitor --> Owned[Caller selected owned output]
```

Real ZoneTree tests accompany provider repairs; TUnit/MTP, process recovery and replicated-cluster qualification come from exact-source Linux runs. Counters describe logical examined work; physical I/O and allocation need separate measurement. Gates and fault semantics cannot be relaxed for throughput.

## Null tombstone versus empty value

REQ-STORAGE-006/009, AC-STORAGE-006, AC-PSW-002..004 and AC-ROC-002/004/005 keep the canonical nullable mutation distinction: an empty value remains a value while Delete remains a tombstone, and no stored bytes are reclassified. The CLR byte-array projection in ZoneTreeTransaction.PrepareChanges uses an explicit null for Delete. `TombstoneValueTests` use an actual store and first prove staged/committed owned and borrowed reads, empty Put versus Delete, exact WAL/base64/null bytes, the frame limit, ordered range, WAL reopen and snapshot record count/reopen. Cache/copy/flush/apply order and NullValueOverheadBytes=2 (a four-byte null replaces two value quotes) are preserved ([ADR-035](../ADR/ADR-035-memory-performance.md) and ADR-041). Qualification requires exact-source unit, process recovery and replicated-cluster SDK/MCP evidence; the projection alone proves no power-loss guarantee or performance gain.

## Storage, journal and recovery contract

Actors: the node-local PartitionHost, canonical apply/read callers and the recovery/backup operator. Actual source: [ZoneTreeStore](../../src/KeyLoad.Storage.ZoneTree/ZoneTreeStore.cs), [runtime](../../src/KeyLoad.Storage.ZoneTree/StorageEngine/Execution/ZoneTreeStoreRuntime.cs), [checkpoint manager](../../src/KeyLoad.Storage.ZoneTree/StorageEngine/Recovery/ZoneTreeCheckpointManager.cs), [KeyCodec](../../src/KeyLoad.Abstractions/Storage/Serialization/KeyCodec.cs), [storage contracts](../../src/KeyLoad.Abstractions/Storage/Contracts/). One physical owner holds the lock, the journal/read-apply gate and the immutable returned values; migrating grains never open the tree.

| Requirement | Acceptance / flows | Test mapping |
|---|---|---|
| REQ-STORAGE-005: journal/checkpoint recovery preserves one complete acknowledged process cut | AC-STORAGE-005: a real process kill at the prepare/publish/install boundaries recovers one whole cut without a partial transaction; a torn tail has declared handling, a corrupt complete frame fails closed; a validated snapshot is never mixed with older state | Existing `FiftySeededRealProcessCrashesPreserveAtomicTransactions` (StorageRecoveryProcessTests), `ProcessKillDuringCheckpointPublicationRecoversOneCompleteGeneration`, `CorruptCompleteFrameFailsClosedInsteadOfBeingDiscardedAsATornTail` in [StoragePublicationRecoveryTests](../../tests/KeyLoad.RecoveryTests/StorageRecovery/Cases/StoragePublicationRecoveryTests.cs) |
| REQ-STORAGE-006: the canonical key/envelope/owned-value format has stable ordering and scope | AC-STORAGE-006: golden keys, escaped UTF-8 components, signed/decimal ordering and scope do not collide; an invalid or corrupt format is rejected and borrowed spans never escape the gate | Existing `GoldenKeysRemainStable`, `TenThousandSeededDecimalKeysRoundTripAndSortNumerically`, `StringsUseUtf8OrdinalOrderingAndEscapedComponentsDoNotCollide` in [KeyCodecTests](../../tests/KeyLoad.UnitTests/StorageRecovery/Cases/KeyCodec/KeyCodecTests.cs); scoped-read cases AC-MP-002 |
| REQ-STORAGE-007: current-format readers reject unsupported or corrupt persisted data before effects | AC-STORAGE-007: real current-format open, recovery, checkpoint install and backup/restore accept supported records; unknown/corrupt mandatory versions fail closed with original bytes and authority unchanged; no conversion or fallback reader is invoked | Existing current-format ZoneTree, RecoveryTests and BackupRestore real-operation cases under [CurrentFormat](StorageRecovery/CurrentFormat.md), [ADR-011](../ADR/ADR-011-current-format.md) and [ADR-116](../ADR/ADR-116-first-release-current-format.md) |

ProcessDurable and ProcessDurable describe the verified declared process failure model; `LocalDurable`/`LocalDurable`, power-loss and endurance are not accepted from process-kill source or test names. The full acknowledgement barrier is [ADR-003](../ADR/ADR-003-durability-ack-barrier.md); committed read views are [ADR-004](../ADR/ADR-004-committed-read-views.md); the versioned keyspace is [ADR-005](../ADR/ADR-005-canonical-keyspace-codec.md); backup/log retention is [ADR-008](../ADR/ADR-008-backup-log-retention.md), [BackupRestore](BackupRestore.md).

Current-code journal, compaction, checkpoint and scoped-visitor changes require exact delivered-source evidence. Source and recovery evidence is qualified through the required TUnit, actual child-process recovery and cluster suites. Maintenance automation and power-loss gates remain explicitly pending. Provider/container composition has one owner; new helper or test scopes do not change the format without an ADR contract.

## Orleans binary atomic WAL

[ADR-057](../ADR/ADR-057-orleans-atomic-wal.md) accepts REQ-STORAGE-015..019 and AC-WAL-001..005. Current commands.wal mutation payloads use generated Orleans binary serialization with the current WAL, identity and unversioned checkpoint contracts under [CurrentFormat](StorageRecovery/CurrentFormat.md). ZoneTree raw-byte Sync WAL and the atomic journals retain their current contracts. Unsupported or corrupt mandatory frames fail closed; no alternate decoder or fallback reader is used. The closed ReadOnlyMemoryOfByteCodec registration keeps nullable value bytes raw rather than using the generic per-byte codec. Fresh normal/scalar, process recovery, replicated-cluster, comparative performance, power-loss/endurance, numeric coverage and independent resource evidence remain required.

|Requirement|Acceptance/test trace|
|---|---|
|REQ-STORAGE-015 binary mutation codec|AC-WAL-001 real-store binary/empty/delete roundtrip and reopen|
|REQ-STORAGE-016 exact bounded payload|AC-WAL-002 FrameBudgetTests plus PreparedTransactionTests cache/replacement/reset/rejected-stage|
|REQ-STORAGE-017 safe replay|AC-WAL-003 checksum/full-consumption/shape/order and unsupported-format rejection; OrleansWalSuccessorTests verifies last legal sequence, terminal overflow rejection, unchanged journal/identity, no premature apply and released ownership; existing real commit crash cuts remain mandatory|
|REQ-STORAGE-018 current-format admission and restore|AC-WAL-004 current checkpoint/empty/refusal and checkpoint install/restore identity preservation|
|REQ-STORAGE-019 authentic qualification|AC-WAL-005 full strict default-configuration build, formatter/governance and exact-SHA GitHub unit/process-recovery/cluster SDK+MCP; speed and power-loss unclaimed|

Complete-frame length mismatch (REQ-STORAGE-017 / AC-WAL-003): `StoreOpenPreflightTests.R12Ac002CompleteCorruptionRejectsBeforeAnyProviderFileChanges` has direct-open and real RecoveryProcess-inspector arguments for an otherwise complete current WAL frame whose declared payload length is one byte shorter than its actual serialized payload. Both paths return the existing exact `ErrorCode.Corruption`, preserve the complete provider file/tree hash inventory and release the owner lock. The test begins with an actual committed ZoneTree record and a valid preceding frame; it adds no frame-size limit and changes no reader behavior. The short-header case is covered by `R12Ac002IncompleteCurrentTailIsOnlyTruncatedByOrdinaryRecovery`, an incomplete-tail recovery case rather than a corruption oracle; existing truncation and committed-prefix assertions are unchanged.

```mermaid
flowchart LR
    Transaction[Owned ordered transaction] --> Codec[Orleans generated binary payload]
    Codec --> Commit[Checksum and disk flush]
    Commit --> Execution[ZoneTree Sync WAL]
    Reopen[Journal recovery] --> Validate[Version checksum complete decode and ordered keys]
    Validate --> Apply[Apply verified mutations]
```

No new UI/API/model format applies.

### Case-to-acceptance clarifications

The protected-principal cases in `tests/KeyLoad.UnitTests/StorageRecovery/Cases/Partition/PartitionHostRecoveryTests.cs` exercise the real pending-image path, which rejects missing/modified protected state on repeated host-open attempts and accepts a valid protected principal after installation. These cases support REQ-STORAGE-004 for the silo-local owner's verified pending-image recovery and ownership path. They are local host/store evidence, not required-copy acknowledgement or cluster evidence, and are not generic format-corruption cases.

`OfflineRegularFileEntryTypeTests`, `OfflineRegularFileIdentityTests`, `OfflineRegularFileLockTests` and `OfflineRegularFileDuplicateLockTests` provide supporting primitive evidence for REQ-STORAGE-015 / AC-SG009P-001..004 through actual regular-entry, identity, BCL lock-interoperation and handle lifetime observations. They do not invoke the guarded RecoveryTests process protocol and cannot establish its complete strict-request, receipt or original-reader settlement contract by themselves.

REQ-STORAGE-015 also maps AC-SG009P-001..004 ([ADR-059](../ADR/ADR-059-isolated-intensive-timeseries.md)). The canonical directory is lexical normalized ordinal equality, without a symlink/rename resistance claim. Every guard test invokes the genuine compiled RecoveryTests process using bounded private JSON and actual failure/data facts; original process exit and both pipe drains precede outer-lock release and ordinary reopen. The cancellation case observes the original ready child, kills/reaps it and checks data after normal reopen. This helper is not the benchmark inspector/control/copy oracle.

## Event append seeded crash cuts

REQ-STORAGE-006/007 and AC-STORAGE-006/007 are preserved through the exact seven during-append process cuts and original child/readers/store-lock ownership frozen in [EventStreams](EventStreams.md) and ADR-002. The recovered doc/event/head/dedup/queue/outbox/receipt and same-ID/healthy-follow-up assertions use real ZoneTree and current serialization. No timeout change, multi-lane or power-loss claim follows.

## store gate diagnostic

REQ-SERIES-022 / AC-SERIES-022 and REQ-STORAGE-GATE-001 / AC-STORAGE-GATE-001 define a per-open-store gate diagnostic. ZoneTreeGateSnapshot carries only the existing ephemeral read SessionId and the ReaderWriterLockSlim.WaitingWriteCount as WaitingWriters. The scalar counts all pending write-gate users, not only commits; it is not a receipt, readiness/authority token, consistent multi-field cut or durability proof. No key, principal, credential, path, payload, callback, event handler, exported metric label or unbounded history exists. Sampling acquires no store gate, scans no records, mutates no storage and allocates no retained collection; the owner must remain open through sampling and cleanup. No HTTP/SDK/MCP diagnostic route is added. storage owns this observation; Orleans still owns actual requests and node-local storage/commit authority.

Whole flow: seed and refresh a literal real bucket; hold its existing authorized budgeted SampleRollupReader inside the actual store Read callback and retain only a detached literal result. Submit exactly one actual stable-ID Drop on a joined worker. Under the unchanged five-second scope bound, observe same-session WaitingWriters>0 while the first read gate remains held; only then release and join the actual reader and writer. Verify the first complete literal bucket, the exact drop receipt kind/revision, unchanged raw/ID/sequence/floor authority, the revision tombstone, stable-ID replay with unchanged full bytes/position, and a genuine CAS recreate/read healthy continuation. The condition is actual queued writer work, not Task.IsCompleted, a pre-call signal or a scheduling delay. Full bytes inside the held gate equal the pre-drop cut. observations and controller/worker errors settle before owned store disposal; primary and cleanup failures are retained.

## Topic purge crash cuts

REQ-EVENT-RETENTION-004 / AC-EVENT-RETENTION-004: the real RecoveryProcess first acknowledges purge through source position 1, then arms the existing CanonicalCrashBoundary for exactly the next Store.Commit position of immutable purge-through 2. Two seeds admit either success (paused checkpoint 3) or exact ResourceExhausted rejection (paused checkpoint 1). Kill only the owned original process after its phase marker; join original exit and both bounded readers, prove exclusive locks and reopen the actual ZoneTree. HeaderWritten/PayloadWritten are pre-flush boundaries; JournalFlushed is after the durable flush returns and before apply, not inside an OS flush syscall; MutationApplied0 and ApplyCompleted are actual apply cuts. A pre-flush cut may recover the old or the whole new cut; a post-flush cut must recover the complete success or rejected receipt, never partial model changes.

The independent oracle requires literal head/events/source positions/digests/doc/queue/paused checkpoint plus the original acknowledged seed receipt, canonical command fingerprint/receipt, exact committed clock and stable same-ID replay with full-store byte equality. Fresh identical/conflicting EventID errors remain distinct. Appending, reading, queue claim and a final reopen follow. No new product seam, provider, journal, format, whitelist, sleep, wider deadline or retry-to-green; process kill is not power-loss proof. The scenario lives in RecoveryProcess EventStreams and RecoveryTests EventStreams with the existing process/readers/cleanup helpers.

## Composite range and unique-index process cuts

REQ-DSTORE-002 / AC-DSTORE-002 and AC-DSTORE-COMPOSITE-PROCESS-001 preserve the equality/range/composite/partition-unique scope. A real four-process RecoveryProcess matrix verifies inserted, replaced, patched, deleted and tombstoned documents, complete composite/ordered-score/unique index images in two atomic partitions, literal membership and exclusive-after-key ranges. Composite equality must report the genuine declared composite index; range Scan proof is distinct from query-planner inequality seeks. A mixed document/event/enqueue unique conflict rolls back every effect; its persisted failure and the successful acknowledged receipt replay unchanged after the crash with complete store-byte and position invariance. A fresh command follows recovery and a fourth reopen preserves it.

The matrix reuses the JournalFlushed cut, acknowledged first kill, original child stdout/stderr and joined cleanup/deadlines. Parent literal tuples are independent of observed index values; the public KeySpace encodes the specified literal keys and never calls the index mutation implementation. No provider doubles, fallback or retry-until-pass. ADR-002 owns command replay; ADR-011 owns atomic journal/recovery.

## maintenance lifetime

REQ-STORAGE-MAINTENANCE-JOIN-001: the node-local ZoneTree owner joins its actual tracked merge thread before releasing tree/journal/owner file handles (disposal waits for jobs). Read-cut join alone does not prove maintenance completion. No product API, timer, dependency, format or public boundary changes.

AC-STORAGE-MAINTENANCE-JOIN-001: MaintenanceLifetimeTests seeds complete independent literal byte records through real atomic Commit, updates/deletes through another Commit, and starts the existing maintainer EvictToDisk. Observe OnDiskSegmentCreated (actual disk record serialization completed on the merger thread), hold only that callback under the existing ten-second lifetime bound, start real owner Dispose, observe the actual same-owner queued writer under a real Read, then its acquired writer gate before releasing the merge callback. Dispose must remain incomplete only after those observations. Release and join the original Dispose, observe the SUCCESS terminal merge and stopped original thread, open every original file exclusively, reopen the same owner with the same position and full literal scan, perform a healthy real Commit and reopen to its complete expected bytes. A pre-call Task state, Start callback, sleep, padded corpus or getter-only check cannot substitute.

One real physical runtime/maintainer is used (ZoneTreeStoreRuntime and ZoneTreeStore internal composition); there is no second provider or storage owner. In the pinned ZoneTree 1.9.8 source OnMergeOperationStarted fires before the merge thread is created, so it is not the work oracle; OnDiskSegmentCreated runs on the merge worker after disk creation, and the maintainer's Dispose waits for tracked threads. The caller and cleanup join before closing barriers or deleting files, preserve primary plus cleanup errors and retain the root on failure. Test cancellation neither skips the join nor fabricates a successful merge.

```mermaid
flowchart LR
  Commit[Actual committed literal records] --> Merge[tracked merge disk created]
  Merge --> Hold[Owned callback held]
  Hold --> Dispose[Real owner dispose enters writer gate]
  Dispose --> Join[Release callback and join original worker]
  Join --> Reopen[Exclusive original files and complete healthy reopen]
```

Pinned Maintainer.Dispose joins merge threads, but its default periodic cache-cleanup Task is not retained or awaited, so the merge case alone cannot qualify periodic-job completion. The following contract owns that remaining boundary.

REQ-STORAGE-MAINTENANCE-JOIN-002: runtime physical sessions own a Orleans grain timer bound to the context which opened their files: a silo-local Grain Service for isolated storage or the ordinary database owner grain for shared storage. The timer synchronously invokes the public execution `ReleaseReadBuffers`, `ReleaseCircularKeyCacheRecords` and `ReleaseCircularValueCacheRecords` APIs under the canonical store read gate. `ZoneTreeMaintenanceLifetime` owns merge maintenance and synchronous sweeps; it starts no Task, thread, thread-pool callback or `PeriodicTimer`. The pinned `ZoneTreeMaintainer` uses `startJobForCleaningInactiveBlockCaches: false`; automatic merge policies and tracked merge joins remain enabled. The centrally admitted default interval is 30 seconds and block lifetime is 1 minute. Session close retires the timer and joins borrowers before synchronous maintainer/tree/journal/owner-lock disposal. Cleanup requested by host lifecycle or DI disposal is queued on the captured owner scheduler and retains its original Task until completion. Staging and offline callers keep their explicit synchronous storage lifecycle and cannot start independent background engine jobs. Complete delivered-source and deployed-filesystem qualification remains separate.

AC-STORAGE-MAINTENANCE-JOIN-002: a real Orleans-hosted database executes authorized resource configuration and a durable command, retaining the exact original receipt, literal committed data and same-command replay oracle. Observe a timer sweep only after all three cleanup APIs return, capture its actual `IGrainContext` through the accessor, and require reference equality with the physical session's owner. Hold that post-sweep callback, start actual Generic Host shutdown and require shutdown to remain pending until the callback is released. Require joined close, exclusive access to all original files, then verify the original receipt, unchanged replay cut and committed data through two cold starts of the same files. Sleep, cancellation alone, a task-state census or a released lock does not qualify.

AC-STORAGE-MAINTENANCE-JOIN-003: inject a bounded failure at the same actual post-sweep callback and observe the original exception identity through real shutdown and disposal. Retain independent merge/handle failures, stop the timer without detaching its current callback, prove exclusive access to the original files, then verify healthy same-root cold replay. A failed close retains the owner's conservative root-release state. Guarded original-store inspection starts no independent maintainer or periodic job.

Traceability: disposal-waits-jobs -> REQ-STORAGE-MAINTENANCE-JOIN-002 -> AC-STORAGE-MAINTENANCE-JOIN-002/003 -> ADR-046 -> `MaintenancePeriodicLifetimeTests` complete operation flows. The owner-process, read-cut lifetime/budget/cancellation, merge-thread and pooled-envelope flows remain required.

AC-STORAGE-MAINTENANCE-JOIN-004: each zero, negative, sub-millisecond or above-range maintenance interval, and each negative or above-range block lifetime, rejects before creating the directory or owning files. The same previously absent path then accepts a valid central policy, a real multi-record commit, complete literal cold-reopen state with unchanged incarnation/position, a healthy additional commit and a second complete cold reopen. `ZoneTreeMaintenancePolicyTests.InvalidMaintenanceDurationsRejectBeforeOwnershipThenHealthyCommitAndColdReopen` owns all six operation flows.

Operational policy comes from the centrally bound and validated `ZoneTreeStorageExecutionOptions`: a range `MaintenanceInterval` of at least one millisecond and a nonnegative range `BlockCacheLifetime`, with 30-second / one-minute defaults. The typed snapshot is frozen before ownership. Linux qualification uses `TimeProvider.System` monotonic elapsed milliseconds from its zero epoch for the read-buffer cutoff (.NET 10 high/low-resolution POSIX clocks share that epoch: Linux MONOTONIC/COARSE, macOS UPTIME_RAW). This disposable cache cutoff is independent of request/read-cut/commit clocks and has only clock-resolution precision. No UTC clock, injected request clock, invented expiry or analyzer suppression is permitted. Windows epoch portability remains a distinct unqualified boundary.

REQ-STORAGE-MAINTENANCE-JOIN-003 / AC-STORAGE-MAINTENANCE-SNAPSHOT-005: every actual store opening receives the complete centrally validated per-store execution policy, including maintenance interval and block lifetime. The direct restore authority owner therefore carries the original centrally bound IOptions from Restore into its private ApplyRestoreAuthorityState and calls the canonical public ResolveExecutionOptions before constructing the staging runtime, rather than the scalar-only WithExecutionSnapshot helper (only the resolver carries the maintenance policy). It must not manufacture an Options.Create wrapper or weaken configuration ownership (KLD0037). The maintenance lifetime freezes and validates that admitted policy before starting work. Explicit per-store frame/snapshot overrides, every effective scalar budget, validation-before-ownership, maintenance lifetime, timer retirement and merge joins, generation replacement and disposal order are preserved. No fallback or default policy, skipped maintenance, exception swallowing, new engine/provider, format, restored authority, public API or changed limit is permitted; the node-local owner and separate journals remain authoritative. The regressions are the whole backup/restore, rejected-input then healthy restore, real CLI children, cold-reopen blob/eventing/metadata/identity flows and real recovery children; ADR-046 owns this private snapshot join and BackupRestore REQ/AC-BACKUP-001/002/003 are unchanged.

## Rollup refresh and drop process cuts

REQ-SERIES-022 / AC-SERIES-022 and [ADR-120](../ADR/ADR-120-bounded-persisted-series-rollups.md) add two real process recovery cases for bounded rollup refresh/drop. The existing RecoveryProcess canonical JournalFlushed hold, CommandIdempotencyProcessChild original output/drain/exit ownership, StorageTrialLease and file-release proof own four distinct child lifetimes. Kill after acknowledged revision 1, then kill the actual in-flight refresh/drop at JournalFlushed; recover revision 2 statistics or tombstone from the current journals. Retain the complete raw sample/identity/sequence/floor bytes, independent full literal samples/statistics/metadata, exact same-ID receipts, a changed-payload Conflict without effects and stable failed replay; then run a healthy refresh/drop/recreate to revision 5 and a separate successful reopen child. No new product diagnostics, alias, limit, timeout or retry loop and no power-loss claim. Every child and both pipe tasks settle before cleanup; primary plus cleanup failure remains observed.

## ANN staged replay process ownership (AC-ANN-007)

`AnnProcessRecoveryTests` owns actual child processes through the existing Aspire recovery entry and finite child/pipe/file-lock settlement. The first stage covers an acknowledged durable pending-page publication before its canonical ACK, then an actual canonical ACK killed at the original JournalFlushed boundary, followed by cold pending-array load, same-ID original receipt recovery and bounded remaining-prefix replay. A later child cold-loads the completed graph, verifies complete literal vector records and performs a genuine healthy explicit vector mutation and replay. Pending arrays never serve readers; recovered nonempty checkpoint receipts establish progress. These process-kill cuts do not establish power-loss durability, arbitrary partial-write coverage or AC-ANN-008 public approximate search qualification.

## storage owner, lease and pooled-value acceptance

REQ-STORAGE-008, REQ-CUT-003, REQ-KEYCODEC-004 and REQ-STORAGE-MAINTENANCE-JOIN-001/002 retain the owner, lease, maintainer/job and pooled-envelope criteria. AC-STORAGE-EXECUTION-SUITE-001 binds the 27-case union of KeyCodecEnvelopeOwnershipTests, MaintenanceLifetimeTests, ReadCutBudgetTests/CancellationTests/ClockTests/ConsistencyTests/LifetimeTests/ReplacementTests, StorageOwnerProcessTests, StoreLifetimeTests, MaintenancePeriodicLifetimeTests and ZoneTreeMaintenancePolicyTests. Normal and scalar each execute 27/27 without skips and settle their child exits/readers/disposal. These complete operations own real node-local files and the genuine separate RecoveryProcess process; Docker replicated-cluster deployment is N/A to this unit lane, so the shared matrix must not build or start Docker resources for it. ADR-046 owns the storage boundary and ADR-117 the direct TUnit entry and fixtures. The complete recovery and genuine replicated-cluster suites remain independently required.

## encoded-frame limit detail

REQ-SR-ENCODED-FRAME-DETAIL-001 / AC-SR-ENCODED-FRAME-DETAIL-001 retain the actual ResourceExhausted guard and limit in ZoneTreeJournalBufferWriter.Advance and use its fixed EncodedTransactionFrameLimitExceeded detail. Raw Stage keeps its original failure detail; no budget/format/recovery ordering changes. Existing FrameBudgetTests.AcWal002IncrementalFrameAccountingMatchesBinaryPayloadAtExactLimits retains every legal coalesced WAL/value/checksum and one-byte-short no-effect assertion, additionally checking the exact owning encoded detail (12 source trials). Ordinary required recovery tests still qualify byte-identical partial-tail semantics.

## Upstream ZoneTree WAL checksum defect

REQ-STORAGE-008, AC-SQ-008 and WAL qualification retain the confirmed checksum findings for the pinned ZoneTree 1.9.8. The owner-authorized public report is [ZoneTree issue 182](https://github.com/ZoneTree/ZoneTree/issues/182): an ARM64 .NET 10 WAL create→dispose→software-read failure, the byte-tail CRC overload defect and a proposed upstream patch. These are development facts, not Linux/x64, release, package, power-loss or KeyLoad acceptance proof. ZoneTree maintainers own the upstream compatibility and release decision. KeyLoad keeps its pinned published dependency and introduces no checksum fallback, migration, local package or consumer workaround; the affected WAL portability gate remains open. The report adds no implementation, schema, topology or dependency boundary (ADR-046 authority and fault contracts suffice).

## WAL checksum dependency and normal-path process recovery

[ADR-126](../ADR/ADR-126-current-zonetree-checksums.md) selects the published ZoneTree 2.0.0 with the centrally pinned System.IO.Hashing 10.0.12 and retains ZoneTree.FullTextSearch 1.0.9. REQ/AC-EXECUTION-WAL-PROFILE-001/002 (cross-profile graceful reopen) are superseded by REQ/AC-EXECUTION-WAL-NORMAL-001 below. REQ-EXECUTION-WAL-PROFILE-003 / AC-EXECUTION-WAL-PROFILE-003 map to solution restore/build/analyzers/formatter plus actual full-text indexing, search, rebuild and reopen regressions; package availability alone closes no gate. This test does not read previous WAL formats, migrate, accept legacy `.wal_crc` or fork checksums.

REQ-EXECUTION-WAL-NORMAL-001 / AC-EXECUTION-WAL-NORMAL-001: SIMD execution is always on, so there is no disabled-intrinsics profile. The test requires SIMD eligibility before opening its store and runs four real processes in seed → first-append → second-append → cold order. `ChecksumProfileRecoveryTests.CurrentWalPreserves257RecordsAndBothAppendsAcrossFourProcesses` has zero parameters and retains the 257 exact corpus rows, both acknowledged command/receipt/outcome appends, original retry/conflicting-body refusal, complete persisted models/outbox, the byte-exact cold cut and the same-root identity. It is process recovery, not power-loss or performance qualification.

Seed commits 257 distinct short UTF8 key/value rows through ZoneTreeStore.Commit and the independent document+event+queue mixed batch through the DatabaseEngine, keeping its generated CommitReceipt/request/outbox evidence. It freezes every key/value row in strict order, the count, committed Store.Position, NodeId/incarnation and read generation in one bounded generated test evidence envelope (alias `keyload.test.execution.checksum-profile-cut` with fields NodeId0/Incarnation1/Position2/ReadGeneration3/Rows4 and row alias `keyload.test.execution.checksum-profile-row` with Key0/Value1); it is test-only, not a database format or recovery authority. Each later phase reopens the same fresh current root and verifies the exact store position, rows, node/incarnation and a non-decreasing read generation before any effect or replay; it then replays the original receipt and a changed-body Conflict, commits an independent fresh document operation (keeping its exact bytes) and freezes the new complete cut. The fourth child proves the whole cut and all 257 rows survive another same-root reopen and closes normally. Every non-seed phase verifies the entire 257-record corpus.

The parent passes only mode/root/phase (three arguments) and never writes DOTNET/COMPlus intrinsics environment; the execution `Vector.IsHardwareAccelerated && (Sse2.IsSupported || AdvSimd.IsSupported)` guard runs before storage acquisition, and unavailable or disabled hardware never counts as success. Old four-argument shapes fail the strict argument-count refusal with no compatibility reader. Completion is phase-only and emitted after store close and failure-ledger settlement. The StorageTrialLease, 90-second execution / 30-second cleanup budgets, bounded pipe readers and initiating/fatal/cleanup exceptions are unchanged. Focused run: `node scripts/Features/TestInfrastructure/run-tests.mjs --KeyLoadTests:Suite=recovery '--KeyLoadTests:Filter=/*/*/ChecksumProfileRecoveryTests/*' --KeyLoadTests:ReportTrx=true --KeyLoadTests:Execution:MaximumParallelTests=50`, built with the ZoneTree 2.0.0 dependency closure.

## Queue lease authority process cuts

REQ/AC-MSG-001/004/005 and REQ/AC-MSG-RETRY-ORDER/JITTER ([ADR-026](../ADR/ADR-026-fenced-delivery-inbox.md), [ADR-028](../ADR/ADR-028-persisted-scheduling-time.md)): two QueueLeaseAuthorityProcessRecoveryTests cases reuse the ordered-retry RecoveryProcess HeaderWritten cut after an acknowledged producer/Receive. The uncommitted sealed NACK must reject an absent or foreign selected lease reference with exact Corruption detail and no whole-store, identity, cut or outcome effect, including repeated and cold calls. A single-row repair restores the complete pre-fault image at cut+2; the same original NACK then commits its recorded jitter and exact receipt. Complete independent original and healthy messages, full body/metadata/counter/order/index checks and replay of all delivery receipts after a second joined cold owner are verified. Process lifetime, cleanup, scan and policy contracts are unchanged; process-kill is not power-loss proof.

## File metadata and physical recovery

ManagedCode.Storage owns physical file bytes; ZoneTree owns file metadata and
provider references under [ADR-038](../ADR/ADR-038-chunked-blob-storage.md).
The raw-part engine, its process-cut scenarios and their exclusive support are
removed. Those results do not qualify the replacement. Physical-object
backup/restore, reopened metadata references, publication cuts and cleanup require
new end-to-end evidence with the selected provider. These gates remain open.

## Qualification gates

Source review, authored cases and local development runs are not qualification. The required evidence is the exact-source Linux run of the normal and scalar unit suites, the process-recovery suite and the Aspire replicated-cluster SDK/MCP suite through the TUnit test entry, with original reports. Process-kill and restart tests do not establish power-loss durability; endurance, power-loss and performance gates remain separately open until their own evidence exists. A suite failure elsewhere does not erase an independently verified mapped criterion, and a mapped criterion does not close the whole feature.

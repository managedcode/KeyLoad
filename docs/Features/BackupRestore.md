# BackupRestore

TASK-BACKUP-INDEXED-DOCUMENT-DEDUP fills the original KL-005 content gap under
REQ-BACKUP-001/002/003. AC-BACKUP-CONTENT-001 requires a current native backup
seeded through actual DatabaseEngine document/index/command operations to restore
into a fresh target, reopen exact canonical document/reference/revision state,
return independently expected native indexed-query memberships and preserve the
original stored dedup bytes. Replaying a pre-backup ID must return the existing
TokenInvalidated outcome without overwrite or duplicate effects because its
recorded incarnation differs from the new restored identity. A new post-restore
command must then replay its exact current-incarnation persisted outcome once,
with no additional effects or store-position change.
Include insert/replace/delete and a rejected unique-conflict batch with no
partial effects; verify the same content after a further close/reopen and a
healthy new command. Raw key/value seeds or getters alone cannot prove it.

AC-BACKUP-CONTENT-002 retains the original offline store cut: manifest position
and verified source position are exactly the acknowledged backup cut; the target
native store position includes the existing single restore-authority commit.
Assert those exact values and unchanged source archive bytes. The existing
new-incarnation restore intentionally clears replica LastApplied, clock and
membership and keeps dispatch paused; that replica authority reset cannot be
advertised as preserving a usable old RF3 applied index. Cluster-cut restoration,
token/lease reconciliation and release qualification remain separate open gates.
Use existing legitimate offline storage/physical-shard ownership APIs when
opening DatabaseEngine/QueryEngine on the restored data; no fabricated catalog,
authorization bypass, replica-authority replay or replacement query path is
permitted. Report any genuine missing product path before changing its contract.

ADR-046/048 and the current native restore contract own this test-only stage;
there is no new format, migration, compatibility path, dependency or public API.
Canonical ownership is UnitTests BackupRestore Cases/Fixtures/Assertions with
cohesive actual roles; frontend and new transport are N/A to the offline gap.
Root freezes/reviews/joins/runs native normal/scalar and source-bound Linux;
a Luna worker prepares a private guarded packet. No task closure is claimed
until every original criterion is directly supported by actual evidence.

The native staged-restore caller remains in the BackupRestore slice under
REQ-BACKUP-002 / AC-BACKUP-002. `BackupRestoreStagingJoinTests` directly invokes
the native ZoneTree restore; despite its historical `AcSqlc011` method label, it
does not execute a SQL client operation and is not evidence for AC-SQLC-011.
Its absent/empty-target flows support AC-BACKUP-002 clean-target publication and
the identity/paused-dispatch portion of AC-BACKUP-003, with byte/readability and
cleanup assertions. They do not establish every old cursor/lease invalidation or
operator-reconciliation requirement. Delivered NativeBackupCut negatives and
recovery checks remain distinct. No new format or migration is defined;
exact-source GitHub execution is pending.

AC-CQ-018 preserves the complete original ManagedCode file-storage transfer byte
oracle in ArtifactTests through ordered content equivalence under pinned TUnit.
Both real awaited file reads remain; a partial checksum or reference comparison
does not satisfy byte-identical transfer. GitHub execution remains pending.

The preserving ArtifactTransfer name-resolution join retains its original public
return type, Result<ManagedCode.Storage.Core.Models.BlobMetadata>, through an
explicit provider alias. The enclosing KeyLoad.BlobMetadata does not change this
archive transport contract. Existing transfer regression and strict compiler
binding map to AC-CQ-001/007/018; no dependency or metadata mapping changes.

Status: local verified backup/restore and piecewise archive transfer are present in source; unit and process-recovery test cases exist, but their execution against the current delivered source and GitHub qualification are pending. Full cluster-cut backup and complete capability-state restore reconciliation remain planned.

## Purpose, actors, and entry points

BackupRestore creates an offline backup of canonical storage and restores it into a clean location. Actors are a database administrator and an operator controlling an offline process. Current entry points are `ZoneTreeStore.CreateBackup` and `ZoneTreeStore.Restore`, CLI commands `backup` and `restore`, Cartograph `pack-backup` / `inspect-backup` / `unpack-backup`, `POST /v1/admin/backup` for server-side backup, and `ArtifactTransfer.CopyToFileStorageAsync` for archive transfer. The CLI requires an offline database for backup and restore. This scope does not define a new backup route or online restore endpoint.

## Canonical slice map and boundaries

| Surface | Current source | Target owner |
|---|---|---|
| Storage backup/restore | `src/KeyLoad.Storage.ZoneTree/ZoneTreeStore.cs` delegates to `Features/BackupRestore/ZoneTreeBackupRestore.cs` and its file/restore helpers | Private behavior is owned by `src/KeyLoad.Storage.ZoneTree/Features/BackupRestore/`; the public facade remains the shared entry point |
| Archive and transfer | `src/KeyLoad.Artifacts/Features/BackupRestore/Execution/BackupArtifact.cs`, `ArtifactTransfer.cs` and focused helpers | Same canonical slice; legacy declarations removed |
| CLI | `src/KeyLoad.Cli/Features/BackupRestore/Commands/CliBackupRestore.cs` behind the aggregate Program runner | Slice-local offline operations; exact dispatch/text/disposal parity under AC-CQ-010 and ADR-033 |
| HTTP backup entry | `src/KeyLoad.Server/ApiEndpoints.cs` | Shared server boundary; no public restore route is defined |
| Tests | `tests/KeyLoad.UnitTests/Features/BackupRestore/Cases/ArtifactTests.cs`; `tests/KeyLoad.RecoveryTests/Features/StorageRecovery/Cases/RecoveryTests.cs` | Unit source is slice-local; remaining recovery layout debt targets matching `Features/BackupRestore/` |
| Documentation | This spec; detailed storage/cluster design in `../design/architecture-v0.3.uk.md` | One canonical feature acceptance source |
| UI and public blob API | None | N/A: operators use CLI/admin boundaries; archive chunking is not a user-facing blob contract |
| Official MCP | No backup tool surface verified | Required MCP work is tracked in ClientApi; no tool name or route is inferred here |

Layer-first source locations are migration debt under [ADR-032](../ADR/ADR-032-mcaf-governance.md). BackupRestore owns artifact verification and restore safety, not live replication, blob storage, or power-loss qualification.

Accepted TASK-MP-010L in [ADR-033](../ADR/ADR-033-code-quality.md) owns the strict
artifact-source prerequisite and its canonical source/test migration. AC-CQ-001/
005/006 and AC-MP-012 join the existing AC-BACKUP-001/002 real archive round trips,
negative-file/checksum cases and transfer assertions. Public signatures, archive
bytes/chunks, permissions, cancellation and actual system-clock timestamps stay
unchanged. Source/formatter success remains separate from exact-SHA GitHub runtime
qualification and the still-planned cluster restore gates.

[ADR-046](../ADR/ADR-046-storage-private-owners.md) and TASK-MP-010AF-B accept a
cohesive internal ZoneTreeBackupRestore owner under this slice, borrowing the one
node-local runtime and retaining the existing public facade entry points. Exact
source-only migration, file ownership, unchanged two-file manifest/identity/paused
restore recipe and AC-SQ-005/007 joins are in the [StorageRecovery](StorageRecovery.md)
implementation contract. Existing AC-BACKUP-001/002/003 remain mandatory; this decomposition adds no
cluster-cut, power-loss, bounded-manifest-memory or performance claim.

## Current behavior, accepted target, and planned work

- The current store backup records a verified backup manifest and canonical storage files. Restore verifies the manifest/checksums, requires a clean target, creates a new database incarnation, and pauses dispatch.
- `BackupArtifact.Pack` divides backup files into bounded Cartograph pieces; `Inspect` lists entries; `Unpack` verifies catalog layout and writes to a destination. `ArtifactTransfer` copies an archive through the ManagedCode file-storage abstraction. This is backup artifact transport, not chunked public BlobStorage or partial blob reads.
- `RecoveryTests.VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch` checks restored data, new identity, paused dispatch, private file mode, and rejection after tampering with `commands.wal`; that is native-store corruption coverage, not Cartograph corruption. `ArtifactTests.ChunkedCartographBackupRoundTripsAndManagedCodeStorageTransfersIt` covers multi-piece archive round trip and byte-identical file-storage copy. Real CLI cases now exercise a noncanonical catalog, first/last entry-length mismatch, and missing manifest/identity/journal inputs, with rejected restore attempts and healthy restore/reopen controls. Authored cases remain pending exact-source Linux qualification; they do not measure peak or retained memory.
- A cluster-wide consistent cut needs per-partition cut positions and catalog epoch, coordinated retention pins, and reconciliation of event, outbox, inbox, queue, and group state. That contract is planned; local backup must not be described as satisfying it.
- Restore of an older cut changes incarnation and must not silently resume external dispatch or claim old cursors remain valid. Operator reconciliation and explicit resume remain a planned cluster-level workflow.

## Requirements and acceptance

| Requirement | Measurable acceptance | Existing or planned evidence |
|---|---|---|
| REQ-BACKUP-001: produce and package a verifiable offline backup | AC-BACKUP-001 passes when a backup includes its manifest/checksums, archives in bounded pieces, and round-trips canonical files. Separate planned resource verification must measure bounded streaming/peak-memory behavior before making a memory-bound claim. | Existing test source: `ArtifactTests.ChunkedCartographBackupRoundTripsAndManagedCodeStorageTransfersIt` checks multi-piece round trip and copy. Planned real-file resource-bound verification; current GitHub TUnit qualification pending. |
| REQ-BACKUP-002: restore only verified data to a clean target | AC-BACKUP-002 passes when a valid backup restores canonical data to a clean location; missing/tampered files and nonempty or unsafe destinations fail without publishing a usable partial database. | Authored real-operation source includes `CliBackupRestoreInvalidCatalogTests`, `CliBackupRestoreLengthMismatchTests`, `CliBackupRestoreMissingInputTests`, `CliBackupRestoreFlowTests`, `BackupRestoreStagingJoinTests`, `ArtifactTests`, and `RecoveryTests.VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch`. StagingJoin directly tests native ZoneTree restore into absent/empty targets and supports this AC's clean-target behavior; its `AcSqlc011` name does not make it an SQL-client test. These cases cover noncanonical catalog rejection, actual rejected output restore, missing required inputs, nonempty destination, original-byte/state preservation, and healthy restore/reopen controls. Exact-source Linux execution remains pending; path-traversal/reparse-specific and resource-bound evidence remains distinct and open. |
| REQ-BACKUP-003: fence old identity and pause delivery after restore | AC-BACKUP-003 passes when restore produces a different incarnation, sets dispatch paused, and invalidates old cursor/lease identities until explicit operator reconciliation. | Existing `VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch`; planned auth/feed/lease token invalidation and explicit resume integration cases. |
| REQ-BACKUP-004: restore a declared cluster cut with capability invariants | AC-BACKUP-004 passes when a captured per-partition cut restores document/event/outbox/inbox/queue/group state consistently, reports unavailable history explicitly, and performs no automatic external redelivery before resume. | Planned Docker/Aspire RF3 backup/restore and process-recovery scenarios under KL-042/KL-098; no current test or GitHub artifact establishes this acceptance. |
| REQ-BACKUP-005: bound local metadata and parse the verified identity region once | AC-BSM-001..005: inclusive16KiB manifest/4KiB identity limits, same-owned-region outer/inner checksum, preserved error/destination/lock ordering and real allocation/restore proof | [ADR-048](../ADR/ADR-048-bounded-storage-metadata.md), [acceptance](../ADR/ADR-048-bounded-storage-metadata.md) and [task graph](../ADR/ADR-048-bounded-storage-metadata.md); Metadata* real-file test source and exact-SHA GitHub qualification pending |
| REQ-BACKUP-006: publish an unpacked archive only after complete native validation | AC-BACKUP-006 passes when first/last entry length failures leave an initially absent destination absent or an initially empty destination empty, the real CLI cannot restore either failed output, and the unchanged original archive still restores canonical data with a new incarnation and paused dispatch. All handles and owned cleanup settle; primary and cleanup failures are preserved. | Authored complete real CLI flow: `CliBackupRestoreLengthMismatchTests.AcBackup006CliLengthMismatchCannotPublishRestorablePartialBackup`, plus `CliBackupRestoreMissingInputTests.AcBackup002CliMissingRequiredBackupFilesRejectWithoutPublicationAndRestoreAfterRepair`; see TASK-BACKUP-UNPACK-PUBLICATION-001, TASK-BACKUP-CLI-MISSING-INPUT-002 and [ADR-114](../ADR/ADR-114-verified-artifact-publication.md). Source and tests exist; actual execution and all required Linux qualification remain pending. |

### Functional CLI operation coverage

`BackupRestoreStagingJoinTests.AcSqlc011AbsentTargetPublishesNativeCutAndPreservesEveryBackupByte` and `AcSqlc011ExistingEmptyTargetPublishesNativeCheckpointAndPreservesEveryBackupByte` each have two trailing-separator cases. Their bodies directly call the native restore API, reopen the target, verify new identity/incarnation, paused dispatch, restored data and exact archive bytes. Map them to REQ-BACKUP-002 / AC-BACKUP-002 and the identity/pause portion of REQ-BACKUP-003 / AC-BACKUP-003 only. They do not exercise SQL, so the source's AC-SQLC-011 label is not an acceptance mapping. The cases do not close the complete cursor/lease fencing and operator reconciliation parts of AC-BACKUP-003. Their original native report rows remain failed-cohort historical evidence until current-source normal/scalar qualification.

TASK-GENERAL-OPTIONS-RESTORE-INPUT-001 maps REQ-BACKUP-002 / AC-BACKUP-002 to
`MissingBackupManifestTests`: the real embedded restore normalizes only a missing
manifest or its directory to the existing `FormatUnsupported` Problem and fixed
manifest detail. The other required-file contracts remain intact. Both genuine
missing/empty-directory cases verify unchanged source/valid-backup bytes, original
store identity and data, no published/staged destination, and a healthy following
restore. They passed in the original native Aspire focused118/118 run on2026-10-06;
that focused development result does not qualify full recovery, RF3 or Linux.

TASK-CQ-CLI-BACKUP-FLOW-001 maps REQ-BACKUP-001/002/003 to AC-BACKUP-001/002/003
and AC-CQ-031. It adds genuine Release CLI child-process flows in
tests/KeyLoad.UnitTests/Features/BackupRestore/Cases/CliBackupRestoreFlowTests.cs,
with feature-local Processes/ and Fixtures/ helpers. The actual Aspire-owned
unit and unit-scalar runners own every child, original exit, bounded pipe drain,
temporary file and cleanup. Native validated test execution options supply
operational bounds; no alternate test entry point or fake CLI is permitted.

The positive flow commits and closes real ZoneTree storage, then invokes backup,
pack-backup, inspect-artifact, copy-artifact, unpack-backup and restore through the
actual CLI dispatch. It verifies copied archive bytes, reopened canonical data,
a new incarnation and paused dispatch. Negative flows pass corrupted original
artifacts or nonempty destinations through the same real CLI boundary, observe
the original nonzero exit, preserve existing destination bytes and storage state,
and complete a healthy follow-up operation. CLI dispatch and disposal assertions
remain part of the operation flow; property access alone is not acceptance.

The standalone CLI binds KEYLOAD_STORAGE__ and KEYLOAD_POINTCACHE__ through the
native environment configuration provider. Missing variables preserve the
canonical typed defaults; supplied values are validated before opening storage.
The regression extends TASK-CQ-CLI-BACKUP-FLOW-001 with an actual invalid-budget
backup child, unchanged committed source and absent backup destination, followed
by successful backup and restore. Overrides are confined to that child process;
the test does not mutate the runner environment or compare property accessors.

These cases are functional contributors only after native collection binds
their original executions and assemblies. They do not establish a cluster-wide
cut, old-token fencing, RF3 recovery, power-loss safety, or a measured coverage
percentage. Existing artifact/recovery flows remain mandatory. Existing ADR-033,
ADR-046 and ADR-048 contracts apply unchanged; no new format or public contract
is introduced by this test stage. Root owns the native coverage and source-bound
evidence joins; exact-source Linux qualification remains pending.

TASK-CQ-BACKUP-CATALOG-FLOW-002 and TASK-CQ-BACKUP-LENGTH-FLOW-003 extend
REQ-BACKUP-002 / AC-BACKUP-002 with actual CLI regressions in
`CliBackupRestoreInvalidCatalogTests` and `CliBackupRestoreLengthMismatchTests`.
The first uses Cartograph's native writer with a noncanonical identity entry,
executes rejected unpack, preserves source/archive/backup bytes, and completes
valid unpack/restore with reopened data, new incarnation and paused dispatch.
The second changes only the declared length of the first or last native catalog
entry, executes unpack and then actually tries restoring its output. A failed
unpack must not publish a usable backup, including when the last entry fails
after all payload files were copied. The untouched original must still restore
successfully, and all original CLI children/readers and owned files must settle.
These are complete operation flows under the existing criterion; source presence
does not establish their pass, coverage hits or module closure. A reproduced
production gap requires its failure/publication contract to be frozen in an ADR
before changing archive execution. Existing restore, RF3 and recovery gates stay
mandatory.

TASK-CQ-BACKUP-REJECTED-INPUT-FLOW-004 maps REQ-BACKUP-001/002/003 and
AC-BACKUP-001/002/003 to `ArtifactTransferRejectedInputFlowTests`. It executes the
actual transfer with a directory source and actual pack with a piece size one
byte below the accepted minimum. Both errors must leave source and backup bytes
unchanged and publish no artifact or destination. The same fixture then completes
native pack/inspect, ManagedCode file-storage transfer, unpack and ZoneTree restore,
checks exact archive and backup bytes, and reopens both original seeded and large
records under a new incarnation with dispatch paused. These existing API contracts
need no new ADR; ADR-033 owns functional collection and ADR-046/048 own verified
restore. The case is unqualified until its actual Aspire execution and original
native coverage establish the operation results and source-bound hits.

### Bounded missing-input errors in the offline restore CLI

TASK-BACKUP-CLI-MISSING-INPUT-002 continues REQ-BACKUP-002/006 and
AC-BACKUP-002/006 under [ADR-114](../ADR/ADR-114-verified-artifact-publication.md).
The real rejected-output restore from the interrupted development run failed
its stderr-retention bound before returning the process result. The retained
log proves that overflow, while its actual emitted bytes are unavailable.
Current native restore also exposes expected FileNotFoundException and
DirectoryNotFoundException for missing required backup inputs; the existing
embedded missing-file regression explicitly verifies that native contract.

At the offline CLI restore adapter, translate those two expected missing-input
exception types into the existing KeyLoad Problem JSON with `Corruption`,
detail `The backup is missing a required file.`, and exit code1. Keep the
native embedded storage API and all other exception contracts. Emit no source
path, raw exception, credentials or caller data. Do not add a blanket catch,
prevalidation substitute, fabricated process result or larger output budget.

The shared manifest reader already rejects a missing manifest/directory as
native `FormatUnsupported` with detail `The backup manifest is unsupported.`.
Preserve that existing KeyLoadException without remapping it. Consequently both
failed-unpack restore attempts and the missing-manifest case expect that native
Problem; missing identity/journal inputs exercise the CLI Corruption mapping.

The real first/last length-mismatch workflow must execute both rejected restore
attempts, assert the native FormatUnsupported Problem and actual joined process, retain the
absent/preserved-empty and byte-preservation oracles, then restore the healthy
archive and reopen its exact data/new identity/paused dispatch. A real CLI
missing-input workflow additionally covers each required manifest, identity and
journal file: remove one actual file, execute the rejected restore, verify no
published destination and preserved remaining inputs, replace the original file,
then execute and verify a healthy restore in the same fixture. Exact stderr and
native exits distinguish this intended error contract from the earlier overflow
hypothesis; runtime proof is required before claiming that failure repaired.

Root freezes contracts, reviews the guarded packet and owns integration/tests;
Luna owns only CLI BackupRestore command/message and its complete operation-test
joins. Canonical format/build, Aspire normal/scalar BackupRestore regressions,
functional coverage and required recovery/RF3/Linux gates remain mandatory.

## Negative and boundary flows

### TASK-KL042-CATALOG-001A: atomic first-write roster

REQ-BACKUP-004 / AC-BACKUP-004 first require a durable roster of complete
PartitionRef identities. The supported RF3 topology has one physical shard and
one ordered node-local store/apply cut; a future multiple-physical-shard barrier
is not implemented. Current-format operations register each partition identity
atomically with its first committed effect or explicit placement. No legacy roster backfill or
conversion of an earlier development format is supported. Cluster capture and
restore must observe the complete current roster and continue to reject missing
or corrupt roster state; fresh registration alone is not a cluster-backup
qualification result.

Add a generated native v1 entry with stable alias/IDs for Version, Partition,
FirstSeenStorePosition and FirstSeenAppliedIndex. The key is
KeyCodec.Encode("atomic-partition-catalog", "v1", "partition", the four scope
strings). A positive replicated index is retained separately from local store
position; replicated rows use store position0 so node-local commit offsets cannot
make RF3 canonical rows diverge. Local operations use positive store position and
applied index0. Repeated registration validates
and preserves the original row. Placement remains authoritative in its current
row; the roster must not duplicate physical ownership or invent a second epoch.
The frozen alias is keyload.backup.atomic-partition-catalog-entry.v1; field IDs
are0 Version,1 Partition,2 FirstSeenStorePosition and3 FirstSeenAppliedIndex.

Inside the existing DatabaseEngine atomic command callback, a feature-local
IAtomicTransaction decorator forwards the actual native view/write contract and
records distinct partition identities from staged Put/Delete keys. Reuse
PartitionRecordFamilies.All and the canonical KeyCodec; include destination
partitions of cross-partition graph effects and explicit empty-placement rows.
Recognized malformed/noncanonical scoped keys fail as Corruption. Unknown global
families do not create rows. Outcome-v2 has separate canonical global/unknown
four-component grammars as well as scoped outcomes; match those exact global
keys through the existing KeySpace constructors before interpreting a partition
scope. A tenant literally named global or unknown remains a valid scoped identity.
Reset clears staged candidates. Register candidate
rows with effects and durable outcomes before ValidateCommit, including the
post-reset bounded rejection outcome path. The combined native commit validation
must reject the whole staged image on exhaustion; no acknowledged effect may
omit its roster row. Replays must preserve their original outcome/row identities.
Use the validated DatabaseLimits.MaxBatchMutations and MaxBatchBytes to bound
distinct retained candidate identities/encoded keys, and preserve provider commit
bounds. Do not add arbitrary operational constants or an unbounded collection.
Carry the engine's existing validated OperationLimitsOptions as native
IOptions<DatabaseLimits> into the roster transaction. Do not create a parallel
configuration owner or pass a raw limits policy across this boundary. Tests use
the same explicitly validated options contract. Private helpers whose only actual
caller uses the roster transaction retain that concrete type; all domain indices
and empty-count values have named constants under the unchanged analyzer policy.

Root owns contracts, shared commit integration, policy/source maps and native
gates. Luna may prepare a guarded private packet for Abstractions BackupRestore
Contracts/Serialization, Core BackupRestore Execution/Serialization/Validation,
the exact AtomicCommandCommit join and placement-key identity reuse, plus real
UnitTests BackupRestore cases. No replication/startup/public API/dependency/Git
changes in this stage. Do not add unused Ready flags or capture placeholders.
Current writes atomically register roster rows with effects and durable outcomes;
strictly reject missing or corrupt current roster state. There is no old-format
roster backfill or storage conversion. A cluster backup is available only when
the current roster and all existing capture/restore gates qualify.

Mapped real workflows must commit and reopen several partition/model writes,
verify same-transaction roster/outcome/data, retry without changing first-seen
identity, reject a batch with preserved state, register a durable rejected
outcome, bind an empty placement, exercise a cross-partition destination effect,
and delete data while retaining its monotonic row. Use actual ZoneTree and
authenticated DatabaseEngine operations, not fake transactions or source checks.
Full build/format, Aspire normal/scalar, recovery/RF3 and source-bound Linux
functional coverage remain mandatory. Cluster capture requires a complete,
validated current roster and the existing restore gates. Unsupported format
versions, missing roster state and corrupt roster rows fail closed; no backfill
or conversion of an earlier development format is supported. ADR-008 and the
current-format decisions govern native records and restore behavior.

Reject checksum mismatch, missing canonical files, malformed catalog entries, path traversal/reparse points, nonempty destinations, and a restore whose manifest/version is unsupported. Preserve the last known materialized state when a restore fails. A process-kill or local round trip is not evidence of power-loss durability, a globally consistent multi-partition cut, or recovery of every optional capability.

## ADRs and verification boundary

Related decisions: [ADR-003](../ADR/ADR-003-durability-ack-barrier.md), [ADR-008](../ADR/ADR-008-backup-log-retention.md), [ADR-011](../ADR/ADR-011-current-native-format.md), [ADR-116](../ADR/ADR-116-first-release-current-format.md), and [ADR-030](../ADR/ADR-030-retention-paused-restore.md). Cluster identity and node ownership follow the accepted [ADR-036](../ADR/ADR-036-orleans-foundation.md). The product-level restore sequence is described in [design sections 6, 14, and 44](../design/architecture-v0.3.uk.md).

Unit, process-recovery, and RF3 tests run only in GitHub Actions under the repository CI workflow. Existing test names establish planned traceability only; current delivered-source, cluster restore, endurance, and power-loss gates are distinct and pending. Do not infer user BlobStorage from Cartograph piece sizes or replica snapshot chunk transport.

```mermaid
flowchart LR
    Admin[Offline operator] --> Store[Verified local backup]
    Store --> Manifest[Manifest and checksums]
    Manifest --> Archive[Optional piecewise archive]
    Archive --> Clean[Empty restore target]
    Clean --> Verify[Verify every file]
    Verify --> Identity[New incarnation and dispatch paused]
    Identity --> Resume[Explicit reconciliation and resume]
```


TASK-CLIENT-ADMIN-SDK-PARITY adds only the observable archive portion of
AC-BACKUP-001 through the ClientApi contract and ADR-039. The actual RF3 case uses
SDK and discovered official MCP backup operations after an SDK seed commit,
locates each distinct archive through complete dashboard inventories and actual
voter bind mounts, verifies its native cut, restores/reopens exact canonical
state and joins disposal/exact-root deletion. Ownership:
Client `Features/BackupRestore/Transport/BackupClient.cs`; IntegrationTests
`Features/BackupRestore/Cases/AdminBackupClientParityTests.cs` and
`Helpers/AdminBackupArchiveVerifier.cs`. Root owns native build/format/RF3 and
original delivered Linux evidence. Bounded archive pieces/peak-memory,
cluster-cut and capability-state reconciliation remain separately required; these
receipts do not close AC-BACKUP-001 in full or AC-BACKUP-004. No archive format,
server endpoint, dependency, automatic retry or migration changes are introduced.


Original KL-005 task acceptance is complete on the source-bound Linux Stage VII
cohort documented in [the canonical task status](../implementation/status.json).
The clean target recovers exact documents, index memberships and persisted dedup.
The source and manifest positions equal the acknowledged backup cut; the target
position equals that cut plus the single restore-authority commit. Old replica
LastApplied, clock and membership reset and dispatch remains paused under
AC-BACKUP-CONTENT-002. Corrupted/incomplete backups reject. The receipt retains
all original full-suite failures;
this task closure does not mark the complete feature or later source qualified.

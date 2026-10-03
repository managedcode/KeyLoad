# BackupRestore

TASK-SQLC-NATIVE-JOIN / AC-SQLC-011 under
[ADR-065](../ADR/ADR-065-full-sql-client-compatibility.md) closes the omitted restore
caller for ADR-060's two-argument native backup verifier. This remains the
BackupRestore slice under REQ-BACKUP-002 / AC-BACKUP-002 and AC-IS-004. Root joins
the existing private staged restore; BackupRestoreStagingJoinTests adds genuine
absent/empty-target positive byte/source/readability/identity/pause/cleanup proof,
with all delivered NativeBackupCut negatives and recovery checks retained.
No new format or migration is defined; exact-source GitHub execution is pending.

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
| Archive and transfer | `src/KeyLoad.Artifacts/Features/BackupRestore/BackupArtifact.cs`, `ArtifactTransfer.cs` and focused helpers | Same canonical slice; legacy declarations removed |
| CLI | `src/KeyLoad.Cli/Features/BackupRestore/CliBackupRestore.cs` behind the aggregate Program runner | Slice-local offline operations; exact dispatch/text/disposal parity under AC-CQ-010 and ADR-033 |
| HTTP backup entry | `src/KeyLoad.Server/ApiEndpoints.cs` | Shared server boundary; no public restore route is defined |
| Tests | `tests/KeyLoad.UnitTests/Features/BackupRestore/ArtifactTests.cs`; `tests/KeyLoad.RecoveryTests/RecoveryTests.cs` | Unit source is slice-local; remaining recovery layout debt targets matching `Features/BackupRestore/` |
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
- The `RecoveryTests.VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch` test source checks restored data, new identity, paused dispatch, private file mode, and rejection after tampering with the backup `commands.wal`. This corruption case does not tamper with a Cartograph archive. `ArtifactTests.ChunkedCartographBackupRoundTripsAndManagedCodeStorageTransfersIt` checks a multi-piece archive round trip and byte-identical file-storage copy; it does not measure peak or retained memory.
- A cluster-wide consistent cut needs per-partition cut positions and catalog epoch, coordinated retention pins, and reconciliation of event, outbox, inbox, queue, and group state. That contract is planned; local backup must not be described as satisfying it.
- Restore of an older cut changes incarnation and must not silently resume external dispatch or claim old cursors remain valid. Operator reconciliation and explicit resume remain a planned cluster-level workflow.

## Requirements and acceptance

| Requirement | Measurable acceptance | Existing or planned evidence |
|---|---|---|
| REQ-BACKUP-001: produce and package a verifiable offline backup | AC-BACKUP-001 passes when a backup includes its manifest/checksums, archives in bounded pieces, and round-trips canonical files. Separate planned resource verification must measure bounded streaming/peak-memory behavior before making a memory-bound claim. | Existing test source: `ArtifactTests.ChunkedCartographBackupRoundTripsAndManagedCodeStorageTransfersIt` checks multi-piece round trip and copy. Planned real-file resource-bound verification; current GitHub TUnit qualification pending. |
| REQ-BACKUP-002: restore only verified data to a clean target | AC-BACKUP-002 passes when a valid backup restores canonical data to a clean location; missing/tampered files and nonempty or unsafe destinations fail without publishing a usable partial database. | Existing test source: `RecoveryTests.VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch` checks data restore and rejects a backup whose `commands.wal` is corrupted. Cartograph catalog/archive corruption, clean-target/path-safety, and partial-failure cases remain planned. |
| REQ-BACKUP-003: fence old identity and pause delivery after restore | AC-BACKUP-003 passes when restore produces a different incarnation, sets dispatch paused, and invalidates old cursor/lease identities until explicit operator reconciliation. | Existing `VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch`; planned auth/feed/lease token invalidation and explicit resume integration cases. |
| REQ-BACKUP-004: restore a declared cluster cut with capability invariants | AC-BACKUP-004 passes when a captured per-partition cut restores document/event/outbox/inbox/queue/group state consistently, reports unavailable history explicitly, and performs no automatic external redelivery before resume. | Planned Docker/Aspire RF3 backup/restore and process-recovery scenarios under KL-042/KL-098; no current test or GitHub artifact establishes this acceptance. |
| REQ-BACKUP-005: bound local metadata and parse the verified identity region once | AC-BSM-001..005: inclusive16KiB manifest/4KiB identity limits, same-owned-region outer/inner checksum, preserved error/destination/lock ordering and real allocation/restore proof | [ADR-048](../ADR/ADR-048-bounded-storage-metadata.md), [acceptance](../ADR/ADR-048-bounded-storage-metadata.md) and [task graph](../ADR/ADR-048-bounded-storage-metadata.md); Metadata* real-file test source and exact-SHA GitHub qualification pending |

## Negative and boundary flows

Reject checksum mismatch, missing canonical files, malformed catalog entries, path traversal/reparse points, nonempty destinations, and a restore whose manifest/version is unsupported. Preserve the last known materialized state when a restore fails. A process-kill or local round trip is not evidence of power-loss durability, a globally consistent multi-partition cut, or recovery of every optional capability.

## ADRs and verification boundary

Related decisions: [ADR-003](../ADR/ADR-003-durability-ack-barrier.md), [ADR-008](../ADR/ADR-008-backup-log-retention.md), [ADR-011](../ADR/ADR-011-format-upgrades.md), and [ADR-030](../ADR/ADR-030-retention-paused-restore.md). Cluster identity and node ownership follow the pending [ADR-036](../ADR/ADR-036-orleans-foundation.md). The product-level restore sequence is described in [design sections 6, 14, and 44](../design/architecture-v0.3.uk.md).

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

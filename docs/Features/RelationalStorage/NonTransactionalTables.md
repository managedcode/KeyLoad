# Non-transactional tables

`ResourceDefinition.Transactional` selects the physical data contract of a collection, time-series resource or analytics table. It defaults to `true`. Document-authoritative resources of those kinds may opt into `false` through the existing SDK/MCP resource definition or typed-table creation request. SQL table creation keeps the default. Other resource kinds reject `false` with `Validation` (`NonTableResourcesRejectPlainModeBeforeCreationAndHealthyTableCreationFollows`).

The flag is immutable for a resource lifetime (`NonTransactionalTableTests.TableModeCannotChangeThroughResourcePolicyUpdates`). Policy updates cannot change it. There is no conversion of an existing table or alternate stored-data reader.

Choose the mode when creating the resource:

| Data contract | `Transactional` | Physical data path |
| --- | --- | --- |
| Multi-key atomic effects and stable transactional snapshots | `true` | Native MVCC |
| Independent analytics events or time-series points accepting the plain-write contract below | `false` | Native raw operations |

The existing SDK resource definition selects the mode explicitly:

```csharp
var table = new ResourceDefinition("measurements", ResourceKind.Collection, transactionDomainId)
{
    Transactional = false
};
```

Use `Transactional = true` when the table needs MVCC. Both choices retain the
native WAL. These are creation settings; the API refuses changes to an existing
resource's storage mode.

Transaction selection and disk durability are separate decisions. Turning off
transactions does not turn off WAL or completed-command outcomes. A stronger native
durable-commit API does not make raw multi-key writes atomic. The current
`ProcessDurable` acknowledgement and the published Shared WAL adoption status are
defined in [NativeMvcc](../StorageRecovery/NativeMvcc.md).

The analytics and time-series comparison drivers explicitly create their resources
with `Transactional=false`. Document and relational comparisons retain their
transactional data contract. Comparisons with earlier transactional analytics or
time-series runs therefore include this change of data semantics.

## Native storage and command boundary

Each physical owner opens `tree/` with `OpenOrCreateTransactional` and `plain-tree/` with `OpenOrCreate`. Catalog, persisted authority, command outcomes, canonical watermarks and the local store position stay in the transactional tree. Table rows and their resource data families, including indexes, use the tree selected by their canonical catalog definition. The plain tree uses native `Upsert`/`ForceDelete`, native deletion serialization and its own ZoneTree WAL; it has no MVCC versions or transaction-status records.

Both trees use the native maintainer. KeyLoad has no writer semaphore, read/write storage lock, logical point cache or second maintenance timer. Transactional operations read one native snapshot and use native serializable conflict detection. Pure transactional retries use ZoneTree's `Execute` API.

A command that writes both transactional and non-transactional tables fails `Validation` before physical publication. Schema, authorization, row and index validation run over staged changes before publication. A rejected command may persist its failed outcome and advance the canonical sequence; it must not publish partial domain changes.

For a non-transactional write, `CommitAsync` publishes plain changes through ZoneTree 2.1.0's native `SyncCompressed` WAL before committing the outcome and metadata in MVCC. Both publications survive process termination when they return; acknowledgement needs no per-command disk sync. Thus a durable success outcome cannot precede the plain data it reports. Source entry points are `ZoneTreePlainPublication`, `StorageOperationBatchApply.ApplyStorageOperationsAsync` and the existing commit coordinators. The synchronous atomic-store commit rejects plain data writes explicitly.

## Failure and observation contract

Plain writes do not provide multi-key atomicity, rollback after physical publication, serializable isolation, or a transaction shared with MVCC. Readers may observe partial or newer plain changes while a command settles. A crash or failure before the metadata commit can leave plain changes without an outcome; retrying that command is not an exactly-once guarantee. A native metadata conflict after plain publication is an unknown write outcome and requires recovery. These are explicit consequences of opting out of transactions.

Stored outcomes still deduplicate completed commands and replay only after current authorization. Table keys and user bytes retain their canonical encoding and case. Physical copies remain actual copies of each owner's native files; this flag adds no new cluster runtime or acknowledgement claim.

## Backup and restore

Backup writes `identity.json` and one native `LiveBackup` generation for each tree. Each generation uses `Snapshot`; the two native trees have independent capture cuts. The returned position is read before the captures and is not a globally atomic snapshot of both data contracts. Restore opens both generations under a new identity with dispatch paused. `copy-backup` copies their native layout through ManagedCode.Storage.

## Acceptance

- REQ-REL-NT-001 / AC-REL-NT-001: `NonTransactionalTableTests.PlainRowsAndTransactionalOutcomesKeepTheirNativeTreesAndSurviveColdOpen` requires exact logical row bytes only in the plain tree, outcome bytes only in MVCC, deduplicated replay, cold reads and a healthy following write.
- REQ-REL-NT-002 / AC-REL-NT-002: `MixedTableModesRefuseBothOrdersBeforeEffectsAndHealthyCommandsContinue` requires rejection in either mutation order, no row effects and successful independent commands afterward.
- REQ-REL-NT-003 / AC-REL-NT-003: `ConstraintFailureDiscardsPlainStagingAndUniqueIndexContinuesAfterDelete` retains real uniqueness validation, no failed-row effect, deletion and healthy index-key reuse.
- REQ-REL-NT-004 / AC-REL-NT-004: `NonTransactionalBackupTests` requires native backup/copy/restore of both data contracts, old-cut data, fresh paused identity and a healthy plain write after restore. It does not establish a common capture cut under concurrent writes.

The strict solution build, functional tests, real process recovery, public Aspire SDK/MCP operations, coverage and exact-source Linux Build and Tests remain separate gates. No throughput or power-loss claim follows from this storage selection.

`NonTransactionalTimeSeriesTests` and `NonTransactionalAnalyticsTests` require real
model commands, matching replay, exact native plain-tree bytes, MVCC-only outcomes
and cold public model reads. These are local cases, not replicated qualification.

- REQ-REL-NT-005 / AC-REL-NT-005: policy updates cannot change either original table mode; the original catalog bytes remain exact and a healthy write follows.
- REQ-REL-NT-006 / AC-REL-NT-006: graph, streams, queues, topics and blob resources reject plain mode before catalog creation, then a healthy table configuration succeeds.
- REQ-REL-NT-007 / AC-REL-NT-007: the time-series native placement, replay and cold-read case above.
- REQ-REL-NT-008 / AC-REL-NT-008: the analytics native placement, replay and cold-read case above.
- REQ-REL-NT-009 / AC-REL-NT-009: `NonTransactionalPublicationTests.FailureAfterPlainWalPublicationRetainsDataWithoutOutcomeThenColdOwnerContinues` injects an original failure after native process-persistent plain WAL publication. The owner refuses further work, cold open retains the plain row without a command outcome or metadata-position advance, and an independent healthy write succeeds. This is an injected boundary failure, not power-loss evidence.
- REQ-REL-NT-010 / AC-REL-NT-010: an existing identity opens both original native trees through `Open`, never `OpenOrCreate`. Removing either tree refuses without creating a replacement; restoring the original directory preserves identity, position and plain rows and allows a healthy following write.

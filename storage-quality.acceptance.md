# Storage maintainability acceptance

Goal: the existing node-local storage adapter satisfies the enabled numeric rules
through cohesive private owners while preserving caller-visible storage/recovery.
REQ-STORAGE-008; ADR-046; AC-CQ-008 and AC-MP-001/002/012. Actors: PartitionHost,
IAtomicStore/read/transaction callers, checkpoint/backup operator and recovery CI.
Public ZoneTreeStore, CommitStage, ZoneTreeStoreOptions and interfaces stay exact.

In scope: private provider decomposition, replacement of facade partial behavior,
named constants, real-store lifetime regressions, strict source and exact-SHA CI.
Out of scope: formats/public contracts, node placement or Orleans ownership,
dependencies, tuning/batch limits, new caches/queues/locks, concurrency semantics,
retention/leases, coverage infrastructure, local tests/resources and new exceptions.

Accepted internal contracts:

- ZoneTreeStore remains public IAtomicStore and IKeyValueView. Read invokes the
  callback on that same facade under the sole read gate. The facade delegates
  behavior to internal real components; no legacy/forwarding compatibility type.
- One ZoneTreeStoreRuntime owns Options, Gate, Ownership, Journal, Tree, Maintainer,
  Identity, interlocked Position, Poisoned/Disposed/dispose-start state, ReadCounters
  and one baseline View. Managers borrow it and never dispose/open a second store.
  Runtime/initializer are lead-owned and stay within all numeric limits.
- Startup retains private directory creation, exclusive owner.lock, identity
  checksum/version/key/incarnation validation, tree then journal open, replay,
  maintainer creation and interrupted-file reclamation. Constructor-failure cleanup
  is maintainer -> journal -> tree -> ownership; normal cleanup is maintainer ->
  tree -> journal -> ownership. Every acquired resource must be attempted even if
  an earlier disposal throws; normal dispose remains idempotent and releases gate.
- Journal remains canonical. Preserve52-byte little-endian header, existing magic,
  offsets/checksum, exact JSON mutations, contiguous positions, torn-tail truncation,
  complete-frame fail-closed, Flush(true) before apply, observer order and poison/
  UnknownWriteOutcome. No extra prepare array/replay or per-row wrapper allocation.
- Stateless ZoneTreeCheckpointWriter.Write(string path, ZoneTreeStoreOptions options,
  StoreIdentity identity, long position, long appliedPosition,
  IZoneTree<Memory<byte>,Memory<byte>> tree) retains original batching/bytes/modes/
  observers. ZoneTreeCheckpointReader.Read(FileStream input, ZoneTreeStoreOptions
  options, Action<StorageMutation>? apply = null) retains strict checks and footer
  stop, leaving subsequent WAL readable. Frame helpers use value results, not
  an added per-frame reference wrapper. Caller owns the gate and expected-cut check.
- Shared lead-owned ZoneTreePersistenceFormat defines exact existing constants:
  HeaderLength52; PayloadLengthOffset8; SequenceOffset12; ChecksumOffset20;
  ChecksumLength32; existing WAL/checkpoint magics; CheckpointVersion2;
  FileBufferBytes65536; CheckpointBatchBytes4194304; CheckpointBatchRecords1000;
  StorageValueHeaderBytes1; SystemNamespace and LastAppliedKey; names and error
  messages. Declare original text/values once and reuse them, no changed format.
- Top-level ZoneTreeTransaction owns the original sorted staged records, copies,
  prepared-payload cache and exact frame budget. Baseline view/range reader/cursors
  borrow the same runtime; prefix/exclusive bounds/order/overlay/tombstone handling,
  early stop/cancellation, observers, work budget and logical diagnostics stay exact.
- ZoneTreeCheckpointManager owns existing create/compact/install/verify/reclamation
  orchestration through runtime. Snapshot verification remains gate-free; install
  verifies incoming before locking and staged image under write lock, rejects stale
  or foreign scope, retains ReadGeneration/FormatVersion publication, file swaps,
  recovery-required poisoning and retired-tree handling in the original order.
- BackupRestore owns a separate internal ZoneTreeBackupRestore component. Create
  uses runtime's write gate and same identity/WAL/manifest checksum/copy/flush recipe;
  Restore preserves clean-target verification, exact two files, new identity/private
  signing key, paused dispatch, and the same four authority-state mutations through
  exactly one normal restored public store. No cluster-cut claim or new backup API.

Criteria and test methodology (TUnit/Microsoft.Testing.Platform, GitHub only):

| ID | Pass/fail and flows | Automated proof / assertions |
|---|---|---|
| AC-SQ-001 | Public signatures/interfaces, callback facade identity, bytes/order/errors and nonpersisted diagnostics remain exact | Existing real storage/Core/client tests; new real-store facade identity/read/commit/dispose case; compile all callers |
| AC-SQ-002 | Exactly one node-local lock/tree/WAL/gate; failed constructor releases acquired handles; ordinary double-dispose is safe and lock is reusable | New real complete-header corruption/open-failure then repaired-file reopen and commit; same-path competing owner and dispose/reopen tests; source order review |
| AC-SQ-003 | Acknowledged transactions survive declared process faults as complete cuts; torn-tail and corrupt-complete behavior, sequence/poisoning/observer stages unchanged | Existing real process RecoveryTests and fault observers; frame budget/scoped transaction tests; no power-loss inference |
| AC-SQ-004 | Snapshot/compact/install retains exact codec/digest/batches, EOF/stream boundary and generation/cut semantics; invalid/foreign/stale staged data cannot replace live state | Existing CheckpointTests and process checkpoint-publication/recovery tests; raw before/after codec/source review |
| AC-SQ-005 | Valid local backup restores data under new private identity and paused dispatch; tampered/nonempty/unsupported input rejects, no automatic redelivery | Existing VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch, real artifact/backup suites and source recipe review |
| AC-SQ-006 | Owned/borrowed point/range and staged overlays retain exact data/counters/budgets; cancellation/visitor failure releases iterator/gate and later operations succeed | Existing ScopedRangeReadTests, resource/frame/read-budget/read-diagnostics suites against real ZoneTree, positive/negative/boundary assertions |
| AC-SQ-007 | Every real file/type/unit/depth satisfies400/200/50/3 with all imported SDK/style/XML rules; no new exception, suppression, fake or leftover facade partial behavior | Ordinary enabled development provider/full builds; scoped/full formatter, source/ownership review and static governance; exact-SHA compiler SARIF |
| AC-SQ-008 | Full required real unit/process-recovery/Docker RF3 .NET/MCP qualification passes for the delivered SHA; evidence preserves exact run/jobs/artifacts and limits | Canonical GitHub CI, no skipped suite counts green; numeric coverage/endurance/fault limits stay open until their separate authentic gates pass |

Source review is explicit manual evidence for exact disposal ordering, unchanged
format bytes/constants and absence of second ownership/replay or per-row wrappers.
Existing tests are reused for this behavior-preserving decomposition; new lifetime
cases address real caller flows, not internal implementation mirrors. Coverage must
not be fabricated: the collector/raw per-file/no-decrease gates remain pending in
quality-gates.acceptance.md and cannot be satisfied by this build or old SHA.

Migration: source-only same-repository ownership move to StorageRecovery and
BackupRestore slices; replace old behavior in the same join. No data/public/wire
migration. Rollback the entire private decomposition as one source unit with format
and product gates intact; do not roll back enabled quality policy or add a shim.
Shared dirty native/website/other-adapter files are outside this write scope.

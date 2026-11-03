# StorageRecovery: current contracts

KeyLoad has one current storage and internal request shape. Application records,
serializer payloads and request purposes have no format number, data epoch or
reader-capability negotiation. [ADR-011](../../ADR/ADR-011-current-format.md) and
[ADR-116](../../ADR/ADR-116-first-release-current-format.md) describe the same
current-only policy.

## Identity, encoding and authority

The node-local owner holds `owner.lock`, the binary `identity.json` envelope,
the MVCC tree under `tree/` and the direct tree under `plain-tree/`. ZoneTree
owns its tree, WAL and native backup formats. KeyLoad uses their public APIs.

`StoreIdentity` contains the node ID, authority incarnation, signing key,
durability profile, dispatch-paused state and read generation. It carries no
FormatVersion, KeyCodecVersion or MinimumReaderContract fields. The generated
Orleans payload remains enclosed by the existing identity magic and SHA-256
checksum. Stable aliases and remaining field IDs are retained.

Before opening a tree, identity admission verifies the complete envelope,
checksum, nonempty node/incarnation, 32-byte signing key, nonnegative read
generation and defined durability profile. Configured incarnation/signing
authority must match. Unknown magic is `FormatUnsupported`; corrupt identity
bytes or values are `Corruption`. File ownership, regular-file/link checks,
bounded reads and original failure/cleanup observation remain mandatory.

KeyCodec has one sortable encoding. Its existing prefix marker and scalar tags
validate framing; they select no alternate decoder. Text/ANN source cuts retain
actual node/incarnation, committed/applied positions, read generation,
resource/schema/policy/consumer generations and digests. Constant format copies
are absent. KeyLoad request purposes identify the operation domain directly.

## Requirements and verification

| Requirement | Acceptance and evidence |
| --- | --- |
| REQ-STORAGE-007 / AC-EXECUTION-001 | Real writes, ZoneTree replay, close/reopen, backup and restore preserve exact rows, authority and position. RuntimeJournalReaderLifecycleTests, DiskDeletionTests, PreparedTransactionTests and process-recovery cases exercise these operations. |
| REQ-STORAGE-021 / AC-EXECUTION-002 | Unknown identity magic, invalid checksum or authority fail before effects and preserve original files. RuntimeJournalReaderFenceTests, MissingIdentityTests, guarded-open and backup/restore cases retain these checks. |
| REQ-EXECUTION-004 / AC-EXECUTION-004 | One current reader and typed contract execute directly, without alternate-version selection, conversion, historical images or prior-format settings. |
| REQ-EXECUTION-005 / AC-EXECUTION-005 | Actual process kills/restarts retain acknowledged operations according to the declared durability contract; all owned processes, readers and resources join. Process-kill evidence does not establish power-loss durability. |
| REQ-EXECUTION-006 / AC-EXECUTION-006 | Strict build, formatting, analyzers and relevant functional/recovery/cluster gates pass against the actual combined source. Focused checks do not close complete qualification. |
| REQ-EXECUTION-007 / AC-EXECUTION-007 | Node-root admission rejects unknown entries, wrong entry kinds and links before mutation; valid partial layouts and exclusive physical ownership remain supported. |

Restore is a current product operation: it creates a new node identity,
incarnation and signing key and starts with dispatch paused. The source backup
remains unchanged. Runtime journal bootstrap requires its protected principal,
catalog presence and quota together; content/owner revisions and compare-and-swap
remain real state, independent of removed format tags.

The current node-root inventory permits the regular ownership lock and the
owned storage/search/ANN/text/analytics/backup directories. Each component owns
its internal files. A directory name alone grants no data or permission authority.
Native engine admission, filesystem qualification and delivered-source Linux
functional, recovery, cluster and coverage gates remain separate requirements.

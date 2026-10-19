# Seeded redo corruption operation

This scenario extends the existing StorageRecovery process suite. It covers one complete-frame checksum fault in KeyLoad's current `commands.wal` atomic redo journal over node-local ZoneTree. It does not damage an upstream ZoneTree provider WAL. [StorageRecovery](../StorageRecovery.md), current-format recovery and ADR-117 retain their complete requirements.

```mermaid
flowchart LR
 Seed[Real child: 257 rows and document/event/queue command] --> Close[Join exit and both readers; close handles]
 Close --> Fault[Retain bounded original; flip one payload bit]
 Fault --> Refuse[Two real opens: typed Corruption; conserve all files]
 Refuse --> Repair[Restore exact original bytes on same root]
 Repair --> Healthy[Original first and second append phases; receipt replay]
 Healthy --> Cold[Cold reopen; full corpus and model checks]
 Cold --> Evidence[Unique safe receipt and joined cleanup]
```

## Requirements and acceptance mapping

| Requirement | Criterion and source operation |
|---|---|
| REQ-CHAOS-REDO-001 Supported producer and reproducible fault choice | AC-CHAOS-REDO-001: `SeededWalCorruptionWholeProcessTests` uses saved seeds 1701/1702/1703. The real original RecoveryProcess producer commits 257 corpus rows and its complete document/event/queue command, saving its original receipt and read cut. hardware eligibility remains mandatory. |
| REQ-CHAOS-REDO-002 Damage only closed owned storage | AC-CHAOS-REDO-002: the original process, stdout and stderr are joined and handles are released before capture or damage. The regular nonsymlink journal is bounded to 1 MiB. Independent framing validates the entire original current v4 journal: magic, 52-byte header, length, contiguous sequence, SHA256 payload checksums and final saved cut position. The seed selects one bit in the final payload byte; length and checksum remain unchanged. |
| REQ-CHAOS-REDO-003 Genuine fail-stop without partial application | AC-CHAOS-REDO-003: two actual RecoveryProcess store opens must throw execution `Corruption` with the existing checksum detail. The original child emits a typed refusal signal only from that catch. Every original file hash and keyset is checked after each refusal, with actual maps retained before assertions. Unexpected success, a different error, metadata writes or file-lock leaks remain failures. |
| REQ-CHAOS-REDO-004 Exact repair and complete healthy recovery | AC-CHAOS-REDO-004: repair always attempts exact original bytes under an independent cleanup deadline, flushes through the handle and verifies every original file. No derived tree is deleted. Original first-append, second-append and cold phases validate the complete corpus, models and saved original/follow-up receipts. Immutable identity and original command/receipt/seed-tail evidence hashes are checked after each phase. |
| REQ-CHAOS-REDO-005 Failure ownership and bounded history evidence | AC-CHAOS-REDO-005: every original task and reader is joined; all later phases, exact repair, file/lock evidence and cleanup are independently attempted through a retained failure ledger. A six-phase history binds actual signals, exit status and closed ownership. A unique CreateNew artifact retains seed, offsets, masks, original/damaged/observed file hashes, actual loaded DLL hashes and OS/architecture before trial cleanup. It contains no raw credentials, signing keys, caller payloads or absolute storage paths. |

## Scope and qualification

The new TUnit whole-class selector is `SeededWalCorruptionWholeProcessTests`; all three arguments must execute. The original `ChecksumProfileRecoveryTests` and `StorageRecoveryProcessTests` remain in the process census. Unit controls include `OrleansWalCorruptionTests`, `OrleansWalSuccessorTests`, `StoreOpenPreflightTests`, `CurrentStoragePreservationTests` and `CheckpointTests`. Replicated-cluster controls include the original `ClusterTests`, using real Aspire-owned resources and .NET/official MCP clients.

Complete Linux Unit/process/replicated-cluster qualification is a separate gate. No upstream ZoneTree defect is claimed from source inspection; an actual provider defect requires the authorized matching public upstream report, without a consumer workaround. General distributed-history checking, network partitions, arbitrary pauses, partial I/O classes, provider WAL corruption, endurance and power-reset qualification are outside this scenario. A process exit, restart or kill does not qualify power-loss durability. HTTP/MCP fault injection is N/A: this is private closed-owner recovery injection, with no public corruption endpoint. No performance result follows from this scenario.

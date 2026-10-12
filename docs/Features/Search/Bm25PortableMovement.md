# Search: native BM25 portable partition movement

Status: implementation acceptance scope; compiler, native runtime and Linux/Aspire RF3 qualification remain OPEN. See [current BM25](Bm25CorpusCurrent.md), [PartitionTransfer](../ClusterRouting/PartitionTransfer.md) and ADR-127.

## Scope

New Unit Search `Bm25PortableMovementTests` borrows the existing `ControlledPartitionMovementNativeHosts`: six real Server/Orleans hosts with original RF3 groups, PartitionHost coordinators, native journals and joined cleanup. It is a local native six-host control, not Docker/Aspire or power-loss proof. No new provider, transport, admission, signer or fixture topology is introduced. Public SDK/MCP qualification remains independent.

## Requirements and acceptance

- REQ-BM25-MOVE-001 / AC-BM25-MOVE-001: actual persisted administrator invokes `NativeBm25CorpusMaintenance.PublishAsync` on the real source Silo/PartitionHost. The original verified private phase45 uses the real identity context and `Silo.ExecuteAsync`; assert returned receipt and actual observed raw head bytes/statistics. Original mixed document/vector/event/queue seed remains present. Execute the original native MovePartition parent to actual destination; exact observed raw head bytes survive installed publication and retirement, retained parent receipt replay and healthy destination Batch.
- REQ-BM25-MOVE-002 / AC-BM25-MOVE-002: while the genuine original Retire operation is borrowed, read its actual retained source fence. Invoke production `PartitionMoveFamilyCapture` under the real protected source view. Unsupported FormatVersion0 and malformed null Statistics are controlled persisted faults derived from the actual published head; compare complete six-owner canonical rows, native hardstate/entries and positions AFTER each fault against the rejected read. Restore the exact original observed bytes under the ordered failure ledger; no pre-fault no-effect claim.
- REQ-BM25-MOVE-003 / AC-BM25-MOVE-003: derive controlled target admission inputs from the actual production-captured valid page and retained fence. Production `PartitionMovePageAdmission` rejects unsupported/malformed native headers before any write. Complete six-owner before/after cuts remain exact. These are validator controls, not authenticated RPC rejection evidence; no synthetic phase, receipt or MAC is submitted.
- REQ-BM25-MOVE-004 / AC-BM25-MOVE-004: original parent task and admitted observer are directly joined on success and failure; release before stop and cleanup, retain roots on failure, aggregate primary and all cleanup errors. Existing request lifetime and original capture/read budgets bound all work; no quota or deadline increase.

REQ-BM25-MOVE-001 / AC-BM25-MOVE-001 also binds the actual retained phase45 Current bytes to the exact observed stored head and the full returned CommitReceipt to its validated retained outcome, including original native mutation receipts, entry index, actual incarnation, atomic partition and durability.

- REQ-BM25-MOVE-005 / AC-BM25-MOVE-005: after the genuine parent retires and its same-MoveId receipt replays, before any healthy destination mutation, the original observed head bytes remain stored but the actual authorized destination current-head eligibility read returns null. The same native view independently preserves principal/policy epoch, schema, resource-policy digest and outbox tail while the actual catalog/store owner incarnation differs from the retained source owner. Genuine public SearchEngine.SearchAsync then reads the canonical original document and returns exactly its one-branch RRF score 1/61 under actual destination query options. Complete six-owner native snapshots remain unchanged. This covers the native canonical query branch, not a fabricated projection or SDK/MCP/RF3 qualification.

All criteria map to `ActualServerPublicationMovesExactNativeHeadAndStrictValidatorsRefuseWithoutEffects`. Portable head shape/key/bounds are checked; its historical PayloadDigest is not recomputed without original placement phase. The moved old head remains historical and is not reused as current destination statistics.

```mermaid
flowchart LR
    Seed[Real persisted mixed-model seed] --> Publish[Actual source Silo maintenance call]
    Publish --> Move[Native MovePartition parent]
    Move --> Hold[Genuine original Retire borrow]
    Hold --> Validate[Actual fence and bounded source capture/page validation]
    Validate --> Repair[Exact original-byte repair and no-effect cuts]
    Repair --> Release[Release and directly join original parent]
    Release --> Healthy[Exact destination bytes, receipt replay and healthy native Batch]
```

## Bounded original native scope evidence

REQ-BM25-MOVE-006 / AC-BM25-MOVE-006: the existing original six-host sealed observer may also implement the existing native IGrainRequestPurposeObserver. Silo DI borrows that SAME object; BeforeSign returns null and never rewrites purpose, payload, expiry, context or authority. Each actual node retains at most its last original issuance and first MAC-verified scope rejection, with only node index, fixed UnclassifiedPurpose label and integer original-purpose length, capability kind, operation IDs, incarnation, expiry, actual receiving cut and domain error. No principal, signing key, native payload or credential is copied or printed.

After the original Move task and all original cleanup join, failure output includes this bounded evidence and the initiating KeyLoadException Code/safe Message. The existing Executor maps actual terminal reply Error/SafeDetail to these values; this is surfaced terminal-failure evidence, NOT an independently retained signed reply, RPC stage, or inferred child rejection when no MAC-verified rejection callback occurred. Original exceptions stay in the same ordered failure ledger; output failure is secondary, and successful cases emit no diagnostic inventory. The case and all existing operation, full-image, receipt, authorization and cleanup assertions remain unchanged. Actual native rerun must identify the observed rejector; current R26 TRX has insufficient ErrorCode/stage provenance and does not prove BM25 caused Move failure.

```mermaid
flowchart LR
    Original[Same native Move task] --> Issuance[Existing BeforeSign returns null]
    Issuance --> Validation[Original MAC and scope validation]
    Validation --> Rejected[Existing RejectedAfterMac first safe metadata]
    Original --> Join[Original task and six owned shutdowns join]
    Rejected --> Evidence[Bounded failure-only evidence]
    Join --> Evidence
    Evidence --> Failure[Original initiating failure ledger retained]
```

Privacy refinement: purpose text is neither retained nor printed. The ledger stores only a fixed UnclassifiedPurpose label and its original integer length; equal lengths do not establish equal protocols. Scope/rejection record formatting cannot emit original purpose content, principal, credentials, payload or signing material. This deliberately lower-observability metadata is not proof of a matched purpose or exact rejector.

Explicit purpose-observer precedence: the diagnostic borrowed interface is registered only when the silo service collection contains no existing IGrainRequestPurposeObserver descriptor. An original explicit omission observer remains selected and is not combined, replaced or silently bypassed. The native Build signature allows both owners, so mutual exclusion is not assumed.

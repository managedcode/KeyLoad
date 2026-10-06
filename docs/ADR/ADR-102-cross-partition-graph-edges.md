# ADR-102: Same-owner cross-partition graph edge delivery

Status: Accepted implementation contract; source integration and actual tests remain required. Related REQ/AC-GRAPH-XPART-001..005 and KL-038. See [CrossPartitionEdges](../Features/GraphTraversal/CrossPartitionEdges.md).

## Decision

The existing source atomic partition remains the canonical owner of each graph edge. For a distinct target atomic partition on the same fully validated current physical shard, source mutation commits a revisioned graph-owned delivery intent in its ordinary replicated atomic transaction. Destination reverse adjacency is a derived record applied in a separate ordinary replicated transaction. Source completion is another separate source transaction. We do not claim cross-partition atomic commit. This stage uses the existing authenticated signed-native CQRS envelope and persisted current grants as trust authority; locator fields and original principal/policy metadata never authorize a caller. Because source and target resolve to the same physical store, completion reloads committed receiver high-water state from that store. This trust/completion rule cannot be reused across physical owners.

## Ordered implementation stages and ownership

1. Freeze the stable generated native aliases/IDs and source/version/intent/receiver/completion contracts in GraphTraversal Abstractions. The accepted feature table additionally fixes the generated capacity record, direction enum and canonical fingerprint tuple before their implementation; checked native capacity accounting and the exact tuple are authoritative. Root joins the central native alias and mutation registries.
2. Root joins the `ApplyCrossPartitionReverseEdge` and `CompleteCrossPartitionReverseEdge` cases through the existing mutation validator, persisted authorization, reauthorization, `ApplyMutations`, and Orleans batch resolver. Root also joins `ReadIncomingGraphEdgesRequestV1` through the read validator, `GrainReadKind`, Core read capability, Server route and SDK/official-MCP read-only tool. The worker owns the bounded Core incoming reader and native Unit cases; no shared dispatcher or public route is duplicated.
3. Core source mutation writes canonical edge, source owner version, coalesced target intent and normal outbox entry in one existing transaction. Target apply validates actual source intent, canonical owner state, full physical tuple and current source/target graph grants in the target native transaction; active delivery validates both visible endpoints. It writes the derived reverse row and receiver high-water together.
4. `ReadIncomingGraphEdgesRequestV1` performs one current node-local store read. It admits at most `MaxResults`, scans at most `MaxScanRecords`, charges point/range/result bytes through the existing `ReadExecutionBudgetReadGrant` and `BudgetedReadView`, and checks the original deadline. It verifies current source canonical edge/version plus destination high-water in that read cut and returns only visible, matching rows. If more eligible rows remain than the requested result limit, it returns no page and fails visibly; no partial result or completeness claim is allowed. Its output marks `eventual-reverse.v1`.
5. Source completion validates the actual target receiver state from the same same-owner native view, exact current source intent and current source plus destination graph-write grants before retiring/accounting the intent. Target applied/source completion lost is safe to retry; superseded old messages are harmless stale no-ops. A later separate service stage may automate bounded page/apply/complete calls through native ManagedCode CQRS; no task fanout or network under apply gate.
6. Tests are actual ZoneTree tests for source atomicity, exact aliases/serialization, placement, duplicate/out-of-order/tombstone behavior, target/source separate commits, incoming query privacy/read budgets, revocation, endpoint deletion, capacity, corruption, retry and source completion failure. Root owns authenticated actual RF3 .NET/MCP tests across all three nodes, build, normal/scalar/recovery suites and exact-source CI evidence.

## Limits, rollout and rollback

The source-stage fingerprint correction implements REQ/AC-GRAPH-XPART-001 before
this feature's first qualified release. Root owns the GraphTraversal Serialization
input-tree normalizer and native regression cases. Freeze a fixed independently
owned record and string-occurrence tree before encoding the unchanged generated fingerprint tuple;
CLR reference sharing cannot determine a delivery or corruption decision. Verify
equal digests after separate actual native edge-frame decoding, independently
allocated equal references, and changed full identity/content. Keep all aliases,
field IDs, exact content and global native serialization behavior unchanged.
Invalid digests fail closed; no fallback or automatic rewrite is accepted.
Real restart/RF3 qualification remains required for populated data. Rollback
retains canonical data, intents and high-water records.

```mermaid
sequenceDiagram
    participant R as Request grain
    participant S as Source atomic partition
    participant T as Target atomic partition
    R->>S: Commit canonical edge and bounded intents
    R->>T: Apply locator after current source verification
    T-->>R: Separate committed high-water state
    R->>S: Complete only the current matching intent
    Note over S,T: One validated physical owner; no cross-partition atomic transaction
```

Use `MaxScanRecords` and `MaxBatchBytes` for pending intent and receiver-state capacity, plus `MaxResults`/`MaxScanRecords`/`MaxBatchBytes` per bounded repair page and the original request deadline. Failed reservation prevents source commit. Do not discard unresolved intents or receiver tombstones automatically. Current placement must be validated against the complete same-shard tuple; different owners fail closed. Rollout uses the current native schema without rewriting existing records. Rollback stops new cross-partition writes/repair but retains canonical source records, unresolved intents and target high-water state. No restart is allowed to reinterpret an unknown native envelope as success. Physical movement remains subject to a separate owner-fenced contract and signed receipt.

## Verification and agent roles

The exact public Batch union/discriminator registration is frozen in [CrossPartitionEdges](../Features/GraphTraversal/CrossPartitionEdges.md#mutation-and-delivery-rules). Root joins the existing mutation union/authorization/dispatch; graph worker pins every new per-type native alias and field ID plus public mutation round trips. Delivery remains existing SDK `CommitAsync(CommandRequest)` with no separate write endpoint or dispatcher.

Feature-local worker owns new Abstractions/Core/Unit GraphTraversal contracts and source, not central dispatch/public caller files. Root integrates alias and mutation joins, docs and inventory; root runs exact canonical build, normal/scalar Unit, process recovery and real RF3 .NET/MCP Aspire gates. No document may mark KL-038 implemented or qualified from native unit source alone. Remote-owner transfer, automatic background coordinator, SQL join, frontend and complete KL-038 delivery-window qualification remain open.

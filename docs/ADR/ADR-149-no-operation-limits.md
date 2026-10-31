# ADR-149: No operation limits

Status: Accepted by owner direction 2026-10-24 ("remove these artificial, made-up, non-standard limits"); extended to byte accounting, reply and frame caps by owner direction 2026-10-31 ("performance, not pseudo-important checks"). In source; Linux qualification pending.

## Decision

KeyLoad sets no configured limit on the size or amount of work of one operation.
`KeyLoad:DatabaseLimits` no longer has `MaxDocumentBytes`, `MaxJsonDepth`,
`MaxBatchMutations`, `MaxBatchBytes`, `MaxScanRecords`, `MaxResults`,
`MaxQueryBytes`, `MaxQueryDepth`, `MaxQueryTokens`, `QueryDeadlineSeconds`,
`MaxQueryReadBytes`, `MaxSearchTextTokens`, `MaxProjectionConsumers` or
`MaxProjectionBatchBytes`. The section keeps only the optional
`MaxQueueTransferAcceptAttempts` and `MaxQueueTransferRepairAttempts`. Both are
null by default and must be at least 1 when set; they no longer depend on a scan
limit.

Every refusal that existed only because one of those numbers was exceeded is
gone: the `BudgetExceeded`, `ResourceExhausted` and `Validation` errors for
document size, JSON depth, batch mutation count, batch bytes, scanned records,
result count, SQL text bytes, SQL depth and token count, query read bytes,
search text tokens, projection consumer count, projection batch bytes and fixed
deadlines. An operation runs as far as its data, the caller's cancellation and
real physical constraints allow. Internal request envelopes carry no expiry.

- **Read execution.** `ReadExecutionBudget` carries only the caller's
  cancellation, stage cancellation, the first indeterminate metadata failure and
  its resource-lifetime bindings. It counts no read bytes or records, issues
  no read grants and measures no results. There is no fixed 30-second deadline
  and no result-byte ceiling: a page or result is bounded by its own record
  `Limit` (`MaxMessages`, `MaxEvents`) and by the data it reads.
- **Grain request streams.** The ManagedCode.Communication CQRS stream between
  grains carries typed chunks without measuring their encoded size: there is no
  per-chunk, reply or aggregate byte cap, no serializer scratch bound and no
  frame cap. `GrainRoutingOptions` has only `ConnectionIdleTimeout`. A short-profile stream still carries at most
  `GrainRequestStreamProtocol.MaximumChunks` (two) chunks, Started plus one
  terminal; more chunks are a protocol-shape violation (`OwnershipLost`).
  Long-profile maintenance streams carry their progress frames without a frame
  cap. Orleans uses its own default message size limits; KeyLoad does not
  override `SiloMessagingOptions.MaxMessageBodySize`.
- **Maintenance replay.** ANN, full-text and online full-text maintenance replay
  read projection batches until the projection reports no more entries. There
  is no replay page count and no projection page byte size, and no
  `AnnMaintenanceOptions` or `TextIndexMaintenanceOptions` section.
- **Partition leaves.** Partition query and distributed search plans carry no
  per-leaf read-byte, record or result-byte allowance, and leaf results report no
  read, retained or examined counters. Cancellation of the one operation bounds
  every leaf.
- **Internal reads, advisory and client state.** Administrator credential reads
  and background owner reads carry only cancellation, with no expiry, read-byte,
  record or result allowance. Event feed Orleans PubSub storage serializes a row once and keeps no
  encoded-byte account. The .NET SDK queue worker counts handlers, deliveries and
  calls, not payload bytes.
- **Complete ranges.** A read that needs a complete key range pages through the
  storage provider's own range page (`ZoneTreeStorageExecutionOptions.MaximumRangeRecords`)
  instead of failing when the range holds more records than one scan. The
  storage-owned range options (`MaximumRangeRecords`, `MaximumRangeWorkBytes` and
  the read-cut ceilings) are unchanged and still bound one storage range
  operation.
- **Recursion.** SQL token access, the ScriptDOM slot visitor, the lowering
  contexts, AST predicate normalization, SQL parameter JSON and the INSERT
  expression walkers call `RuntimeHelpers.TryEnsureSufficientExecutionStack`
  (`ExecutionStackGuard`) and fail the request with `Validation` when the stack
  runs short. There is no numeric depth or token limit. JSON parsing uses the
  System.Text.Json default maximum depth (64); KeyLoad defines no JSON depth of
  its own. A query's offset plus limit must fit a 32-bit row position; a larger
  sum is a `Validation` error because no row index beyond it exists.
- **Physical owners.** Values that bounded a real resource moved to that
  resource's owner:
  - the Postgres TLS and admission handshake use
    `GrainRoutingOptions.ConnectionIdleTimeout`, matching PostgreSQL
    `authentication_timeout`; established wire transport has no deadline;
  - Postgres SQL text is bounded by `PostgresSessionOptions.MaximumFrameBytes`
    (Query messages) and `MaximumRetainedBytes` (prepared statements);
  - full-text manifest file reads and writes are bounded by
    `TextExecutionOptions.MaximumDiskBytes`;
  - full-text read cuts (online pins and captured search reads) are clamped to
    the ZoneTree storage read-cut ceilings;
  - the group-commit journal frame budget still derives from
    `ZoneTreeStorageExecutionOptions.MaxFrameBytes`.
- **Analytics.** ZoneTree.Analytics generations size their ingestion queue by the
  actual row count, do not truncate dimension values, and have no query timeout
  except the caller's explicit expiry.
- **Capability manifest.** `QueryCapabilityManifest`, returned by the SDK, MCP and
  HTTP query capabilities, no longer has `MaxRows`, `MaxCandidates`, `MaxDepth` or
  `MaxCandidateBytes` or `MaxBytes`. Explain rows no longer report `ScanBudget`.
- **Remote queue transfers.** The transfer-retention capacity limit and the
  `TransferRetention` accept-failure kind are removed. The accept dependency
  snapshot no longer records `TransferStoredRecords`, `TransferStoredBytes`,
  `MaxBatchBytes` or `MaxScanRecords`. Queue storage quota failures, enforced by
  the queue's own `MaxStoredMessages` and `MaxStoredBytes` policy, are unchanged.
- **Comparisons.** The load-test AppHost sets no `KeyLoad__DatabaseLimits__`
  environment values, and the benchmark deployment options
  `DocumentKeyLoadMaximumScanRecords` and `DocumentKeyLoadMaximumQueryReadBytes`
  are removed.

Caller-supplied bounds stay: a request's own `Limit`, graph depth,
vertex and edge caps and expiry are honored exactly, and the server
sets no maximum on them beyond the typed `QueryExecutionOptions`,
`GraphExecutionOptions` and messaging options that the owning features declare.
[ADR-145](ADR-145-no-request-throttling.md) (no request throttling) and the
server-wide request body limit are unchanged.

## Consequences

- Large documents, batches, scans, results and SQL text succeed when the hardware
  and the physical storage ceilings allow them. A reply larger than the transport
  can carry fails in the transport, not at an invented operation limit.
- A long operation runs until it completes, the caller cancels it or its explicit
  expiry passes. A caller that needs a time bound sets an expiry.
- Deep SQL, AST and JSON nesting fails only when the execution stack would
  overflow (or, for JSON, beyond the System.Text.Json default depth), with
  `Validation` and no partial effect.
- Operators size hardware and storage range pages for their load; there is no
  per-operation knob to tune instead.
- The removed `DatabaseLimits` and `GrainRoutingOptions` members, their
  configuration keys and their wire fields in the capability manifest, the
  change feed, event stream and queue receive requests, Explain rows and the
  remote transfer accept dependency are gone; configuration that still sets them
  is ignored.
- A test that asserted a removed refusal is removed with it. Complete unit,
  process recovery, Aspire cluster and delivered-source Linux qualification of
  this change remain open.

Related: [ADR-010](ADR-010-query-budgets-security.md), [ADR-035](ADR-035-memory-performance.md), [ADR-145](ADR-145-no-request-throttling.md), [ADR-147](ADR-147-id-ordered-query-continuation.md).

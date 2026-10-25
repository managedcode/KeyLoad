# ADR-149: No operation limits

Status: Accepted by owner direction 2026-10-24 ("remove these artificial, made-up, non-standard limits"). In source; Linux qualification pending.

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
deadlines. An operation runs as far as its data, the caller's cancellation, the
caller's explicit expiries and real physical constraints allow.

- **Read accounting.** `ReadExecutionBudget` starts with no configured bound. It
  accounts read bytes and records and stops on caller cancellation and on
  explicit expiries (the request envelope `ExpiresAt` and caller-supplied
  expiries). There is no fixed 30-second deadline. Result bytes are bounded only
  by the real reply ceilings (`GrainRoutingOptions.MaximumReplyBytes` and the
  related grain stream ceilings) and by the optional
  `QueryExecutionOptions.MaximumResultBytes`.
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
  - projection replay pages are sized by `GrainRoutingOptions.MaximumReplyBytes`,
    with no record count;
  - multi-lane receive results, event feed hints and event feed pubsub rows are
    bounded by `GrainRoutingOptions.MaximumReplyBytes`;
  - Orleans `SiloMessagingOptions.MaxMessageBodySize` equals
    `GrainRoutingOptions.MaximumCompletedBytes`;
  - the Postgres wire transport deadlines and the TLS and admission handshake use
    `GrainRoutingOptions.RequestLifetime`;
  - Postgres SQL text is bounded by `PostgresSessionOptions.MaximumFrameBytes`
    (Query messages) and `MaximumRetainedBytes` (prepared statements);
  - full-text manifest file reads and writes are bounded by
    `TextExecutionOptions.MaximumDiskBytes`;
  - full-text read cuts (online pins and captured search reads) are clamped to
    the ZoneTree storage read-cut ceilings;
  - the ANN public read reservation sizes its encoded reply scratch from
    `GrainRoutingOptions.MaximumReplyBytes`;
  - the group-commit journal frame budget still derives from
    `ZoneTreeStorageExecutionOptions.MaxFrameBytes`.
- **Analytics.** ZoneTree.Analytics generations size their ingestion queue by the
  actual row count, do not truncate dimension values, and have no query timeout
  except the caller's explicit expiry.
- **Capability manifest.** `QueryCapabilityManifest`, returned by the SDK, MCP and
  HTTP query capabilities, no longer has `MaxRows`, `MaxCandidates`, `MaxDepth` or
  `MaxCandidateBytes`. `MaxBytes` reports `QueryExecutionOptions.MaximumResultBytes`,
  or `int.MaxValue` when no result ceiling is configured. Explain rows no longer
  report `ScanBudget`.
- **Remote queue transfers.** The transfer-retention capacity limit and the
  `TransferRetention` accept-failure kind are removed. The accept dependency
  snapshot no longer records `TransferStoredRecords`, `TransferStoredBytes`,
  `MaxBatchBytes` or `MaxScanRecords`. Queue storage quota failures, enforced by
  the queue's own `MaxStoredMessages` and `MaxStoredBytes` policy, are unchanged.
- **Comparisons.** The load-test AppHost sets no `KeyLoad__DatabaseLimits__`
  environment values, and the benchmark deployment options
  `DocumentKeyLoadMaximumScanRecords` and `DocumentKeyLoadMaximumQueryReadBytes`
  are removed.

Caller-supplied bounds stay: a request's own `Limit`, `MaxBytes`, graph depth,
vertex and edge caps and expiry are honored exactly, and the server
sets no maximum on them beyond the typed `QueryExecutionOptions`,
`GraphExecutionOptions` and messaging options that the owning features declare.
[ADR-145](ADR-145-no-request-throttling.md) (no request throttling) and the
server-wide request body limit are unchanged.

## Consequences

- Large documents, batches, scans, results and SQL text succeed when the hardware
  and the physical reply and storage ceilings allow them. A request that would
  produce a reply above a grain reply ceiling fails at that ceiling, not at an
  invented operation limit.
- A long operation runs until it completes, the caller cancels it or its explicit
  expiry passes. A caller that needs a time bound sets an expiry.
- Deep SQL, AST and JSON nesting fails only when the execution stack would
  overflow (or, for JSON, beyond the System.Text.Json default depth), with
  `Validation` and no partial effect.
- Operators size hardware, storage range pages and reply ceilings for their load;
  there is no per-operation knob to tune instead.
- The removed `DatabaseLimits` members, their configuration keys and their wire
  fields in the capability manifest, Explain rows and the remote transfer accept
  dependency are gone; configuration that still sets them is ignored.
- A test that asserted a removed refusal is removed with it. Complete unit,
  process recovery, Aspire cluster and delivered-source Linux qualification of
  this change remain open.

Related: [ADR-010](ADR-010-query-budgets-security.md), [ADR-035](ADR-035-memory-performance.md), [ADR-145](ADR-145-no-request-throttling.md), [ADR-147](ADR-147-id-ordered-query-continuation.md).

using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlTypedInsertRf3State(McpPersistedIdentity Identity, SqlOperationRequest Created,
    CommitReceipt Receipt, SqlOperationRequest Failed, AtomicPartitionPlacementResolution Owner, bool EpochFenced = false,
    SqlOperationRequest? Repaired = null, CommitReceipt? RepairedReceipt = null,
    SqlOperationRequest? Cancelled = null, CommitReceipt? CancelledReceipt = null, int ColdRows = 0, SqlTypedInsertRf3ColdReceipt[]? ColdReceipts = null);

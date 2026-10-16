using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record UserAnalyticsHostCut(StoreIdentity Identity, long Position, string[] Rows,
    ReplicatedOperation[] Originals, CommitReceipt[] Receipts, string SigningKey);

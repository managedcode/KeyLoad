using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record UserAnalyticsHostCut(StoreIdentity Identity, long Position, string[] Rows,
    Command<DatabaseCommand>[] Originals, CommitReceipt[] Receipts, string SigningKey);

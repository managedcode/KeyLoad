using ManagedCode.Communication.Commands;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record UserAnalyticsHostCut(StoreIdentity Identity, long Position, string[] Rows,
    Command<DatabaseCommand>[] Originals, CommitReceipt[] Receipts, string SigningKey);

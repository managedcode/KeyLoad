using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record TestDatabaseConcurrentCloseSeed(PartitionRef Partition, Command<DatabaseCommand> Original,
    Result<object> Result, CommitReceipt ExpectedReceipt, StoreIdentity Identity, long LocalPosition, string[] Canonical);

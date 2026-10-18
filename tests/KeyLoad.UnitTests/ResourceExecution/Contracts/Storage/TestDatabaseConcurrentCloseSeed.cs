using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record TestDatabaseConcurrentCloseSeed(PartitionRef Partition, Command<DatabaseCommand> Original,
    Result<object> Result, CommitReceipt ExpectedReceipt, StoreIdentity Identity, long LocalPosition, string[] Canonical);

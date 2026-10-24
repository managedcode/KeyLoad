using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed record BoundLeaseSeeded(RequestCqrsHostedRuntimeFixture Leader,
    RequestCqrsHostedRuntimeFixture Other, PhysicalShardRecord PhysicalOwner,
    Command<DatabaseCommand> ResourceCreation, Command<DatabaseCommand> Write, CommitReceipt Receipt,
    Dictionary<RequestCqrsHostedRuntimeFixture, (long AppliedPosition, long Position, string[] Records)> Images);

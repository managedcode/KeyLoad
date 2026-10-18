using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeLocalChildLifetimeSeeded(RequestCqrsHostedRuntimeFixture Leader,
    RequestCqrsHostedRuntimeFixture Other, PhysicalShardRecord PhysicalOwner,
    Command<DatabaseCommand> ResourceCreation, Command<DatabaseCommand> Write, CommitReceipt Receipt,
    Dictionary<RequestCqrsHostedRuntimeFixture, (long Position, string[] Records)> Images);

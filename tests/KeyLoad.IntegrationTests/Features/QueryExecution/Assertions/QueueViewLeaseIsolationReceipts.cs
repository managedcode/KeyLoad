using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationReceipts
{
    internal static async Task SeedAsync(RequestCqrsRf3Callers root, SqlModelViewRf3Scenario scenario, CancellationToken token)
    {
        await AssertAsync(root, scenario, scenario.Receipt, scenario.Original.CommandId,
        [new(QueueViewLeaseIsolationProtocol.AppendKind, SqlModelViewRf3Scenario.StreamSet,
            SqlModelViewRf3Scenario.StreamId, QueueViewLeaseIsolationProtocol.LeasedVersion),
         new(QueueViewLeaseIsolationProtocol.EnqueueKind, SqlModelViewRf3Scenario.Queue,
            SqlModelViewRf3Scenario.FirstMessageId, QueueViewLeaseIsolationProtocol.InitialVersion),
         new(QueueViewLeaseIsolationProtocol.EnqueueKind, SqlModelViewRf3Scenario.Queue,
            SqlModelViewRf3Scenario.SecondMessageId, QueueViewLeaseIsolationProtocol.InitialVersion)], token);
    }

    internal static async Task AssertAsync(RequestCqrsRf3Callers root, SqlModelViewRf3Scenario scenario, CommitReceipt actual,
        Guid command, System.Collections.Immutable.ImmutableArray<MutationReceipt> mutations, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(QueueViewLeaseIsolationProtocol.One, scenario.Partition), token));
        await Assert.That(actual.Token.Position).IsGreaterThan((long)QueueViewLeaseIsolationProtocol.Empty);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command,
            new(placement.Incarnation, scenario.Partition.AtomicPartitionId, actual.Token.Position, placement.PlacementEpoch),
            mutations, DurabilityProfile.QuorumProcessDurable), actual);
    }
}

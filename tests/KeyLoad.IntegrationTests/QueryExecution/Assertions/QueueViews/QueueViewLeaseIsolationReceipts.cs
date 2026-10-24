
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationReceipts
{
    internal static async Task SeedAsync(SqlModelViewClusterScenario scenario)
    {
        await AssertAsync(scenario, scenario.Receipt, scenario.Original.CommandId,
        [new(QueueViewLeaseIsolationProtocol.AppendKind, SqlModelViewClusterScenario.StreamSet,
            SqlModelViewClusterScenario.StreamId, QueueViewLeaseIsolationProtocol.LeasedVersion),
         new(QueueViewLeaseIsolationProtocol.EnqueueKind, SqlModelViewClusterScenario.Queue,
            SqlModelViewClusterScenario.FirstMessageId, QueueViewLeaseIsolationProtocol.InitialVersion),
         new(QueueViewLeaseIsolationProtocol.EnqueueKind, SqlModelViewClusterScenario.Queue,
            SqlModelViewClusterScenario.SecondMessageId, QueueViewLeaseIsolationProtocol.InitialVersion)]);
    }

    internal static async Task AssertAsync(SqlModelViewClusterScenario scenario, CommitReceipt actual,
        Guid command, System.Collections.Immutable.ImmutableArray<MutationReceipt> mutations)
    {
        await Assert.That(actual.Token.Position).IsGreaterThan((long)QueueViewLeaseIsolationProtocol.Empty);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command,
            new(actual.Token.Incarnation, scenario.Partition.AtomicPartitionId, actual.Token.Position),
            mutations, DurabilityProfile.ProcessDurable, 3), actual);
    }
}

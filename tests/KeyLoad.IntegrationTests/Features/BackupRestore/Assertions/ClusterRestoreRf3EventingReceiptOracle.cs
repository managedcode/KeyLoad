using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingReceiptOracle
{
    private const string AckKind = "Ack";

    internal static async Task GroupAckAsync(Guid commandId, ClusterRestoreRf3EventingState state,
        PhysicalShardRecord owner, long position, long leaseVersion, CommitReceipt actual)
    {
        await Assert.That(actual.Token.Position).IsGreaterThan(state.CurrentCommitPosition);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(commandId, new(owner.Incarnation,
            state.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch),
            [new(AckKind, ClusterRestoreRf3EventingProtocol.Topic,
                position.ToString(System.Globalization.CultureInfo.InvariantCulture), leaseVersion)],
            DurabilityProfile.QuorumProcessDurable), actual);
        state.CurrentCommitPosition = actual.Token.Position;
    }

    internal static async Task QueueAckAsync(Guid commandId, ClusterRestoreRf3EventingState state,
        PhysicalShardRecord owner, CommitReceipt actual, MessageInspection expected)
    {
        await Assert.That(actual.Token.Position).IsGreaterThan(state.CurrentCommitPosition);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(commandId, new(owner.Incarnation,
            state.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch),
            [new(AckKind, ClusterRestoreRf3EventingProtocol.HealthyQueue, ClusterRestoreRf3EventingProtocol.Healthy,
                expected.Metadata.StateVersion)], DurabilityProfile.QuorumProcessDurable), actual);
        state.CurrentCommitPosition = actual.Token.Position;
    }
}

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasClusterReceipts
{
    private const long InitialRevision = 1;
    private const long EmptyPosition = 0;

    internal static async Task SeedAsync(CommitReceipt receipt, Guid commandId, QueueLaneRef lane)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        MutationReceipt[] expected = [new(QueueDispatchCasClusterProtocol.EnqueueKind, lane.Queue,
            QueueDispatchCasClusterProtocol.Message, InitialRevision)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations.ToArray()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

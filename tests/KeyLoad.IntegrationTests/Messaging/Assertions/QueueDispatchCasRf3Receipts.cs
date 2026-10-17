namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasRf3Receipts
{
    private const long InitialRevision = 1;
    private const long EmptyPosition = 0;

    internal static async Task SeedAsync(CommitReceipt receipt, Guid commandId, QueueLaneRef lane,
        AtomicPartitionPlacementResolution placement)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        MutationReceipt[] expected = [new(QueueDispatchCasRf3Protocol.EnqueueKind, lane.Queue,
            QueueDispatchCasRf3Protocol.Message, InitialRevision)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations.ToArray()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Verifies native source fence publication preserves the immutable acknowledged original mixed outcome.</summary>
internal static class ControlledPartitionMovementFenceStateAssertions
{
    private const long FencedReplicaIndex = 13;

    internal static async Task RetainedAsync(ControlledPartitionMovementNode source, byte[] originalReceipt,
        long initialPosition, CancellationToken cancellationToken)
    {
        var expectedIndex = checked(FencedReplicaIndex + source.AdditionalSeedEntries);
        var position = checked(initialPosition + expectedIndex);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(expectedIndex);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        var image = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var replay = source.Journal.Submit(ControlledPartitionMovementCorpus.SeedOperation(source.Database),
            cancellationToken);
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(replay, originalReceipt);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(expectedIndex);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(image)).IsTrue();
    }
}

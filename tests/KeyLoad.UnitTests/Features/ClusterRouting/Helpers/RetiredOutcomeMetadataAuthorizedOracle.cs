namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RetiredOutcomeMetadataAuthorizedOracle
{
    internal static async Task CorruptionAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ReplicatedOperation original, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        var sourceIndex = source.Journal.Log.State.LastIndex;
        var targetIndex = target.Journal.Log.State.LastIndex;
        var denied = source.Database.ResolveOutcome(original);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(denied.NativeValue).IsNull();
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(sourceIndex);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage, StringComparer.Ordinal)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage, StringComparer.Ordinal)).IsTrue();
    }
}

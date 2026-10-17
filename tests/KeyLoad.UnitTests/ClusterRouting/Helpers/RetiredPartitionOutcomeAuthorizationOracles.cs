namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RetiredPartitionOutcomeAuthorizationOracles
{
    internal static async Task DeniedAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ReplicatedOperation original, ErrorCode expected, ControlledPartitionMovementOutcomeAuthority authority,
        CancellationToken cancellationToken)
    {
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        var sourceIndex = source.Journal.Log.State.LastIndex;
        var targetIndex = target.Journal.Log.State.LastIndex;
        cancellationToken.ThrowIfCancellationRequested();
        var denied = source.Database.ResolveOutcome(original);
        await Assert.That(denied.Error).IsEqualTo(expected);
        await Assert.That(denied.NativeValue).IsNull();
        await Assert.That(denied.Json).IsNull();
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(sourceIndex);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage, StringComparer.Ordinal)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage, StringComparer.Ordinal)).IsTrue();
        await authority.RemainsControlOwnedAsync(source, target);
    }
}

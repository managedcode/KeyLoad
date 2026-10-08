namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Full genuine owner image and native store/journal/apply cuts, captured before an admission denial.</summary>
internal sealed record ControlledPartitionMovementExpiryOwnerState(string[] Image, long Position,
    long LastIndex, long CommittedIndex, long Applied)
{
    internal static ControlledPartitionMovementExpiryOwnerState Capture(ControlledPartitionMovementNode owner)
        => new(ControlledPartitionMovementRawImage.Bytes(owner.Store), owner.Store.Position,
            owner.Journal.Log.State.LastIndex, owner.Journal.Log.State.CommittedIndex, owner.Database.LastApplied);

    internal async Task AssertUnchangedAsync(ControlledPartitionMovementNode owner)
    {
        await Assert.That(owner.Store.Position).IsEqualTo(Position);
        await Assert.That(owner.Journal.Log.State.LastIndex).IsEqualTo(LastIndex);
        await Assert.That(owner.Journal.Log.State.CommittedIndex).IsEqualTo(CommittedIndex);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(Applied);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(owner.Store)
            .SequenceEqual(Image, StringComparer.Ordinal)).IsTrue();
    }
}

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Joins the actual retained source capability before its real terminal abort journal can be issued.</summary>
internal static class ControlledPartitionMovementSourceAbortClosure
{
    private const int EmptyCount = 0;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementCaptureRuntime actualOwner)
    {
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        await actualOwner.Source.CloseMoveAsync(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementPrepareRequest.MoveId);
        actualOwner.Source.RequireMoveJoined(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementPrepareRequest.MoveId);
        await Assert.That(actualOwner.Memory.RetainedBytes).IsEqualTo((long)EmptyCount);
        await Assert.That(actualOwner.Memory.RetainedEntries).IsEqualTo(EmptyCount);
        await Assert.That(actualOwner.Memory.ActiveReservations).IsEqualTo(EmptyCount);
        await Assert.That(actualOwner.Memory.Closed).IsFalse();
        ControlledPartitionMovementProcessScope.Current?.RegisterClosedSource(actualOwner);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }
}

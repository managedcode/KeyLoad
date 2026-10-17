namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Rejects parent and private phase minting through the real standalone public Core factory.</summary>
internal static class ControlledPartitionMovementPublicBoundaryAssertions
{
    private const string Unsupported = "The operation is unsupported.";
    private const long OriginalPlacementRevision = 0;
    private static readonly Guid MoveId = Guid.Parse("e94e9d88-0bdb-4d63-a362-fc6895f2b1dc");

    internal static async Task RejectAsync(ControlledPartitionMovementNode source)
    {
        var request = new PartitionMoveRequest(MoveId, ControlledPartitionMovementCorpus.Partition,
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner.PhysicalShardId,
            OriginalPlacementRevision, PartitionMoveMode.Transfer);
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        await RejectAsync(source, OperationKind.MovePartition, NativeSerialization.Serialize(request));
        await RejectAsync(source, OperationKind.PartitionMovementPhase, NativeSerialization.Serialize(request));
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(before)).IsTrue();
    }

    private static async Task RejectAsync(ControlledPartitionMovementNode source, OperationKind kind, byte[] payload)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.CreateNativeOperation(
            kind, MoveId, PhysicalShardCatalogFixture.RootPrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), payload));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(error.Message).IsEqualTo(Unsupported);
    }
}

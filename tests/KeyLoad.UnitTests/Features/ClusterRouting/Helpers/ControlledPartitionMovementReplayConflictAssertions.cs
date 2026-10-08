namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Preserves original full-partition receipt identity when changed content reuses its command ID.</summary>
internal static class ControlledPartitionMovementReplayConflictAssertions
{
    private const string ChangedJson = "{\"text\":\"Київ knowledge\",\"state\":\"changed\"}";
    private const string Conflict = "The command ID was already used with different content.";
    private const int DocumentMutationOrdinal = 0;

    internal static async Task RejectAsync(ControlledPartitionMovementNode source, byte[] originalReceipt,
        CancellationToken cancellationToken)
    {
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var seed = ControlledPartitionMovementCorpus.Seed();
        var changes = seed.Mutations.SetItem(DocumentMutationOrdinal,
            new PutDocument(ControlledPartitionMovementCorpus.Collection,
                ControlledPartitionMovementCorpus.DocumentId, ChangedJson));
        var changed = seed with { Mutations = changes };
        var operation = source.Database.CreateNativeOperation(OperationKind.Batch,
            ControlledPartitionMovementCorpus.SeedId, PhysicalShardCatalogFixture.RootPrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(changed));
        var rejected = source.Journal.Submit(operation, cancellationToken);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(rejected.SafeDetail).IsEqualTo(Conflict);
        var replay = source.Journal.Submit(operation, cancellationToken);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(rejected))).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(before)).IsTrue();
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(source.Journal.Submit(
            ControlledPartitionMovementCorpus.SeedOperation(source.Database), cancellationToken), originalReceipt);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(before)).IsTrue();
    }
}

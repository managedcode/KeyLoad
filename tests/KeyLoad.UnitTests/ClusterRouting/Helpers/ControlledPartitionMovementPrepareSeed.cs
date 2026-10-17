namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Publishes independently verified mixed and blob inputs through the original replica journal.</summary>
internal static class ControlledPartitionMovementPrepareSeed
{
    private const long OriginalSeedReplicaIndex = 6;
    private const long MixedAndBlobReplicaIndex = 10;
    private const long InitialEpoch = 1;

    internal static async Task<(ControlledPartitionMovementOutcomeAuthority Authority, BlobMetadata Blob,
        DateTimeOffset RecordedAt, byte[] Receipt)> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        CancellationToken cancellationToken)
    {
        var seeded = ControlledPartitionMovementSetup.SeedConfigured(source, target, corpus,
            out var recordedAt, cancellationToken);
        var originalReceipt = await ControlledPartitionMovementReceiptAssertions.SeedAsync(seeded,
            new(corpus.Control.Owner.Incarnation, ControlledPartitionMovementCorpus.Partition.AtomicPartitionId,
                OriginalSeedReplicaIndex, InitialEpoch), DurabilityProfile.ProcessDurable);
        var authority = new ControlledPartitionMovementOutcomeAuthority(source,
            ControlledPartitionMovementCorpus.SeedOperation(source.Database, recordedAt));
        var blob = ControlledPartitionMovementBlobSeed.Publish(source, cancellationToken);
        var metadata = await ControlledPartitionMovementBlobAssertions.PublishedAsync(blob.Result,
            blob.Request, blob.EvaluatedAt, new(corpus.Control.Owner.Incarnation,
                ControlledPartitionMovementCorpus.Partition.AtomicPartitionId, MixedAndBlobReplicaIndex,
                InitialEpoch), DurabilityProfile.ProcessDurable);
        await ControlledPartitionMovementBlobAssertions.ReadAsync(source, metadata, cancellationToken);
        await ControlledPartitionMovementModelAssertions.ReadAsync(source, recordedAt, source.Store.Position,
            cancellationToken);
        return (authority, metadata, recordedAt, originalReceipt);
    }
}

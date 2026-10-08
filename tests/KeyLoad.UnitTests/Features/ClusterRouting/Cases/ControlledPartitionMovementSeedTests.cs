using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementSeedTests
{
    private const long SourceSeedCommands = 6;
    private const long BlobSeedCommands = 4;
    private const long OwnershipEpoch = 1;

    [Test]
    public async Task ParentAndPrivateMintingRejectBeforeNativeMixedAndBlobJournalSeedReplaysExactly()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ControlledPartitionMovementNode(PhysicalOwnerDirectoryWholeFlow.Control.Owner);
            using var target = new ControlledPartitionMovementNode(PhysicalOwnerDirectoryWholeFlow.Destination.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var token = TestContext.Current!.Execution.CancellationToken;
                await ControlledPartitionMovementPublicBoundaryAssertions.RejectAsync(source);
                var initial = source.Store.Position;
                var seeded = ControlledPartitionMovementSetup.Seed(source, target, out var recordedAt, token);
                var expectedPosition = checked(initial + SourceSeedCommands);
                await Assert.That(source.Store.Position).IsEqualTo(expectedPosition);
                var expectedToken = new CommitToken(PhysicalOwnerDirectoryWholeFlow.Control.Owner.Incarnation,
                    ControlledPartitionMovementCorpus.Partition.AtomicPartitionId, SourceSeedCommands, OwnershipEpoch);
                var original = await ControlledPartitionMovementReceiptAssertions.SeedAsync(seeded,
                    expectedToken, DurabilityProfile.ProcessDurable);
                var authority = new ControlledPartitionMovementOutcomeAuthority(source,
                    ControlledPartitionMovementCorpus.SeedOperation(source.Database, recordedAt));
                var image = ControlledPartitionMovementRawImage.Bytes(source.Store);
                var replay = source.Journal.Submit(ControlledPartitionMovementCorpus.SeedOperation(source.Database), token);
                await ControlledPartitionMovementReceiptAssertions.ReplayAsync(replay, original);
                await Assert.That(source.Store.Position).IsEqualTo(expectedPosition);
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(image)).IsTrue();
                await ControlledPartitionMovementModelAssertions.ReadAsync(source, recordedAt, expectedPosition, token);
                await ControlledPartitionMovementReplayConflictAssertions.RejectAsync(source, original, token);
                var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
                var blob = ControlledPartitionMovementBlobSeed.Publish(source, token);
                var blobPosition = checked(expectedPosition + BlobSeedCommands);
                await Assert.That(source.Store.Position).IsEqualTo(blobPosition);
                var metadata = await ControlledPartitionMovementBlobAssertions.PublishedAsync(blob.Result,
                    blob.Request, blob.EvaluatedAt, expectedToken with
                    { Position = SourceSeedCommands + BlobSeedCommands }, DurabilityProfile.ProcessDurable);
                await ControlledPartitionMovementBlobAssertions.ReadAsync(source, metadata, token);
                var blobImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
                var blobOperation = source.Database.CreateNativeOperation(OperationKind.CompleteBlobUpload,
                    ControlledPartitionMovementBlobSeed.CompleteId, PhysicalShardCatalogFixture.RootPrincipalId,
                    blob.EvaluatedAt, NativeSerialization.Serialize(blob.Request));
                await ControlledPartitionMovementBlobAssertions.ReplayAsync(source.Journal.Submit(blobOperation, token),
                    NativeSerialization.Serialize(blob.Result));
                await Assert.That(source.Store.Position).IsEqualTo(blobPosition);
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(blobImage)).IsTrue();
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
                await authority.RemainsControlOwnedAsync(source, target);
                source.Reopen();
                target.Reopen();
                await authority.RemainsControlOwnedAsync(source, target);
                await ControlledPartitionMovementBlobAssertions.ReadAsync(source, metadata, token);
                await ControlledPartitionMovementModelAssertions.ReadAsync(source, recordedAt, blobPosition, token);
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(blobImage)).IsTrue();
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

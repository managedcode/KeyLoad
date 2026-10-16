using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryImportCold
{
    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        D2ResourceInventoryImportResult installed, ResourceLifetimeV1 lifetime,
        (ControlledPartitionMovementOutcomeAuthority Authority, BlobMetadata Blob, DateTimeOffset RecordedAt, byte[] Receipt) seeded,
        bool unknownTarget, CancellationToken token)
    {
        var sourceCut = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetCut = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        await VerifyAsync(source, target, corpus, installed, lifetime, seeded, unknownTarget, token);
        for (var pass = D2ResourceInventoryImportData.Empty; pass < D2ResourceInventoryData.TwoColdOwners; pass++)
        {
            source.Reopen();
            target.Reopen();
            await sourceCut.AssertUnchangedAsync(source);
            await targetCut.AssertUnchangedAsync(target);
            await VerifyAsync(source, target, corpus, installed, lifetime, seeded, unknownTarget, token);
            await sourceCut.AssertUnchangedAsync(source);
            await targetCut.AssertUnchangedAsync(target);
        }
    }

    private static async Task VerifyAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        D2ResourceInventoryImportResult installed, ResourceLifetimeV1 lifetime,
        (ControlledPartitionMovementOutcomeAuthority Authority, BlobMetadata Blob, DateTimeOffset RecordedAt, byte[] Receipt) seeded,
        bool unknownTarget, CancellationToken token)
    {
        await ControlledPartitionMovementInstalledModelImage.AssertAsync(target, corpus, installed.Image.Pages);
        var repeated = target.Journal.Submit(installed.ConcurrentOperation, token);
        await D2ResourceInventoryAssertions.EqualAsync(installed.ConcurrentResult, repeated);
        await D2ResourceInventoryAssertions.EqualAsync(installed.ConcurrentResult.Get<CommitReceipt>(),
            repeated.Get<CommitReceipt>());
        await D2ResourceInventoryAssertions.EqualAsync(installed.InstalledResult,
            target.Journal.Submit(installed.InstalledOperation, token));
        var document = target.Database.GetDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            new(D2ResourceInventoryImportData.Partition, D2ResourceInventoryImportData.Resource,
                D2ResourceInventoryImportData.DocumentId), cancellationToken: token);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Json).IsEqualTo(D2ResourceInventoryImportData.Json);
        await Assert.That(document.Revision).IsEqualTo(D2ResourceInventoryImportData.Single);
        await seeded.Authority.RemainsControlOwnedAsync(source, target);
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(source.Journal.Submit(
            ControlledPartitionMovementCorpus.SeedOperation(source.Database, seeded.RecordedAt), token), seeded.Receipt);
        await ControlledPartitionMovementBlobAssertions.ReadAsync(source, seeded.Blob, token);
        await ControlledPartitionMovementModelAssertions.ReadAsync(source, seeded.RecordedAt, source.Store.Position, token);
        if (unknownTarget)
        {
            await D2ResourceInventoryImportQuery.RefusedAsync(target, corpus, token);
        }
        else
        {
            await D2ResourceInventoryImportQuery.HealthyAsync(target, corpus, lifetime, token);
        }
    }
}

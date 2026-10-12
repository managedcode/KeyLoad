using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobSupersessionTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        TestDatabase? fixture = null;
        ZoneTreeStore? cold = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            fixture = new();
            var first = await BlobRangeIntegrityOperations.Publish(fixture);
            await BlobRangeIntegrityAssertions.Healthy(fixture.Database, fixture.Store, first);
            var original = fixture.Store.Read(view => BlobRecordReader.Get<BlobState>(view,
                BlobKeys.State(first.Blob, first.UploadId)))
                ?? throw new InvalidOperationException("The genuine v1 publication must own its state.");
            var second = await BlobSupersessionPublication.PublishAsync(fixture.Database, first, token);
            await BlobSupersessionAssertions.RequireAsync(fixture.Database, fixture.Store, first, second, original, token);
            var image = QueueWholeFlowStorage.Bytes(fixture.Store);
            var position = fixture.Store.Position;
            fixture.Store.Dispose();
            for (var reopen = 0; reopen < 2; reopen++)
            {
                cold = new(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
                var owner = fixture.CreateConfiguredDatabase(cold);
                await BlobWholeFlowAssertions.Unchanged(cold, position, image);
                await BlobSupersessionAssertions.RequireAsync(owner, cold, first, second, original, token);
                await BlobWholeFlowAssertions.Unchanged(cold, position, image);
                cold.Dispose();
                cold = null;
            }
        }, failures);
        ServerFailureObserver.Observe(() => cold?.Dispose(), failures);
        ServerFailureObserver.Observe(() => fixture?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

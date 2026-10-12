using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPublishedRecordIntegrityTrial
{
    internal static async Task RunAsync(int fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TestDatabase? fixture = null;
        ZoneTreeStore? cold = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new TestDatabase();
            var published = await BlobRangeIntegrityOperations.Publish(fixture);
            await BlobRangeIntegrityAssertions.Healthy(fixture.Database, fixture.Store, published);
            var originalImage = QueueWholeFlowStorage.Bytes(fixture.Store);
            var damaged = BlobPublishedRecordIntegrityFaults.Install(fixture.Store, published, fault);
            await BlobPublishedRecordIntegrityAssertions.Refused(fixture.Database, fixture.Store, published, fault);
            var faultPosition = fixture.Store.Position;
            var faultImage = QueueWholeFlowStorage.Bytes(fixture.Store);
            fixture.Store.Dispose();
            cold = Open(fixture);
            var owner = fixture.CreateConfiguredDatabase(cold);
            await BlobWholeFlowAssertions.Unchanged(cold, faultPosition, faultImage);
            await BlobPublishedRecordIntegrityAssertions.Refused(owner, cold, published, fault);
            BlobPublishedRecordIntegrityFaults.Repair(cold, damaged);
            await BlobWholeFlowAssertions.Unchanged(cold, faultPosition + 1L, originalImage);
            await BlobRangeIntegrityAssertions.Healthy(owner, cold, published);
            var repairedPosition = cold.Position;
            var repairedImage = QueueWholeFlowStorage.Bytes(cold);
            cold.Dispose();
            cold = null;
            cold = Open(fixture);
            owner = fixture.CreateConfiguredDatabase(cold);
            await BlobWholeFlowAssertions.Unchanged(cold, repairedPosition, repairedImage);
            await BlobRangeIntegrityAssertions.Healthy(owner, cold, published);
            await BlobWholeFlowAssertions.Unchanged(cold, repairedPosition, repairedImage);
        }, failures);
        if (cold is not null)
        { ServerFailureObserver.Observe(cold.Dispose, failures); }
        if (fixture is not null)
        { ServerFailureObserver.Observe(fixture.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ZoneTreeStore Open(TestDatabase fixture)
        => new(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
}

using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRangePolicyColdTrial
{
    internal const string Reader = "blob-range-cold-reader";

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TestDatabase? fixture = null;
        ZoneTreeStore? cold = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            var published = await BlobRangeIntegrityOperations.Publish(fixture);
            var principal = new PrincipalRecord(Reader, fixture.Partition.TenantId,
                [new(fixture.Partition.DatabaseId, BlobRangeIntegrityFixture.Resource, Capability.BlobRead)], [])
            { PolicyEpoch = 1L };
            await ConfigureAsync(fixture.Database, principal, token);
            await BlobRangePolicyColdAssertions.Healthy(fixture.Database, fixture.Store, published, token);
            await BlobRangeIntegrityAssertions.Healthy(fixture.Database, fixture.Store, published);
            await ConfigureAsync(fixture.Database, principal with { Revoked = true, PolicyEpoch = 2L }, token);
            await BlobRangePolicyColdAssertions.Denied(fixture.Database, fixture.Store, published, token);
            var revokedPosition = fixture.Store.Position;
            var revokedImage = QueueWholeFlowStorage.Bytes(fixture.Store);
            fixture.Store.Dispose();
            cold = Open(fixture);
            var owner = fixture.CreateConfiguredDatabase(cold);
            await BlobWholeFlowAssertions.Unchanged(cold, revokedPosition, revokedImage);
            await BlobRangePolicyColdAssertions.Denied(owner, cold, published, token);
            await ConfigureAsync(owner, principal with { PolicyEpoch = 3L }, token);
            await Assert.That(cold.Position).IsEqualTo(revokedPosition + 1L);
            await BlobRangePolicyColdAssertions.Healthy(owner, cold, published, token);
            await BlobRangeIntegrityAssertions.Healthy(owner, cold, published);
            var restoredPosition = cold.Position;
            var restoredImage = QueueWholeFlowStorage.Bytes(cold);
            cold.Dispose();
            cold = null;
            cold = Open(fixture);
            owner = fixture.CreateConfiguredDatabase(cold);
            await BlobWholeFlowAssertions.Unchanged(cold, restoredPosition, restoredImage);
            await BlobRangePolicyColdAssertions.Healthy(owner, cold, published, token);
            await BlobRangeIntegrityAssertions.Healthy(owner, cold, published);
            await BlobWholeFlowAssertions.Unchanged(cold, restoredPosition, restoredImage);
        }, failures);
        ServerFailureObserver.Observe(() => cold?.Dispose(), failures);
        ServerFailureObserver.Observe(() => fixture?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ZoneTreeStore Open(TestDatabase fixture)
        => new(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());

    private static async Task ConfigureAsync(DatabaseEngine database, PrincipalRecord principal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var operation = database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            BlobRangeIntegrityFixture.Principal, database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = database.Apply(operation).Get<PrincipalRecord>();
        await BlobWholeFlowAssertions.Exact(result, principal);
    }
}

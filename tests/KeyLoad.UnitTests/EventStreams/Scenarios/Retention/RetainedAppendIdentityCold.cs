using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class RetainedAppendIdentityCold
{
    internal static async Task RepairAsync(RetainedAppendIdentityFixture fixture, RetainedAppendIdentityFault fault,
        CommandRequest command, string[] original, string[] damaged, long position, StoreIdentity identity, CancellationToken token)
    {
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            store = new(new(fixture.Harness.Directory), fixture.Harness.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            var database = fixture.Harness.CreateConfiguredDatabase(store);
            await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityOperations.RefuseAsync(database,
                store, command, damaged, position + 1, identity), failures);
            ServerFailureObserver.Observe(() => fault.Repair(store), failures);
            await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityAssertions.ConservedAsync(store,
                original, position + 2, identity), failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.CommitAsync(database, store, command, RetainedAppendIdentityOperations.Effects("repaired", 2));
                await RetainedAppendIdentityModel.RequireAsync(database, store, fixture, false);
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityOperations.ReplayAsync(database, store, fixture), failures);
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(() =>
            {
                fixture.ColdImage = QueueWholeFlowStorage.Bytes(store);
                fixture.ColdPosition = store.Position;
                fixture.ColdIdentity = NativeReadCutStoreStateAssertions.SnapshotIdentity(store);
            }, failures);
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task FollowingAsync(RetainedAppendIdentityFixture fixture, CancellationToken token)
    {
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            store = new(new(fixture.Harness.Directory), fixture.Harness.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            var database = fixture.Harness.CreateConfiguredDatabase(store);
            await RetainedAppendIdentityAssertions.ConservedAsync(store, fixture.ColdImage,
                fixture.ColdPosition, fixture.ColdIdentity);
            await RetainedAppendIdentityModel.RequireAsync(database, store, fixture, false);
            await RetainedAppendIdentityOperations.ReplayAsync(database, store, fixture);
            var command = fixture.Producer("healthy", "following", ExpectedStreamRevision.Exact(2));
            await fixture.CommitAsync(database, store, command, RetainedAppendIdentityOperations.Effects("healthy", 3));
            await RetainedAppendIdentityModel.RequireAsync(database, store, fixture, true);
            await RetainedAppendIdentityOperations.ReplayAsync(database, store, fixture);
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

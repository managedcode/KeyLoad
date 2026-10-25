using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SparseCompositePrefixCold
{
    internal static async Task RunAsync(TestDatabase fixture, bool includeNull, bool includeMissing,
        List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        var bytes = JsonDefaults.Serialize(identity);
        fixture.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var cold = ZoneTreeExistingStore.Open(new(fixture.Directory)
                {
                    Incarnation = identity.Incarnation
                }, identity.NodeId, fixture.StorageExecution);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await SqlOffsetRefusals.UnchangedAsync(cold, image, position, bytes);
                    var owner = fixture.CreateConfiguredDatabase(cold);
                    await ServerFailureObserver.ObserveAsync(() => SparseCompositePrefixOperations.RunAsync(owner, cold,
                        fixture.Partition, includeNull, includeMissing, originals), failures);
                    await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(cold, image, position, bytes), failures);
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

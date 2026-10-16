using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativeFaults
{
    internal static async Task TargetLifetimeAsync(TestDatabase source, TestDatabase target,
        NativeAnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest pin, string? resource = null,
        Func<AnnSearchPage, long, Task>? healthy = null)
    {
        var selected = resource ?? AnnSeedTestSupport.Collection;
        var key = ResourceLifetimeCatalog.Key(target.Partition.TenantId, target.Partition.DatabaseId, selected);
        var original = target.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(AnnLifetimeNativeProtocol.Missing);
        var other = target.Store.Read(view => view.ReadOwnedValue(ResourceLifetimeCatalog.Key(target.Partition.TenantId,
            target.Partition.DatabaseId, AnnLifetimeNativeProtocol.OtherResource))) ?? throw new InvalidOperationException(AnnLifetimeNativeProtocol.Missing);
        var sourceRows = NativeAnnMaintenanceTestData.Snapshot(source);
        var sourceCut = source.Store.Position;
        // Negative persisted-state fault injection only; every positive resource was created by native commands.
        foreach (var corrupt in new byte[]?[] { other, null, [] })
        {
            var failures = new List<Exception>();
            try
            {
                await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    Set(target, key, corrupt);
                    var rows = NativeAnnMaintenanceTestData.Snapshot(target);
                    var cut = target.Store.Position;
                    var request = AnnLifetimeNativeRead.Request(target, pin);
                    if (resource is null)
                    {
                        request = request with
                        {
                            IndexGeneration = AnnLifetimeNativeProtocol.ForeignGeneration
                        };
                    }
                    await AnnLifetimeNativeAssertions.RejectedAsync(() => AnnLifetimeNativeRead.SearchAsync(source, target, runtime, request), ErrorCode.Corruption);
                    await AnnLifetimeNativeAssertions.UnchangedAsync(source, sourceRows, sourceCut, target, rows, cut);
                }, failures);
            }
            finally { KeyLoad.Server.ServerFailureObserver.Observe(() => Set(target, key, original), failures); }
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            var page = await AnnLifetimeNativeRead.SearchAsync(source, target, runtime, AnnLifetimeNativeRead.Request(target, pin));
            if (healthy is null)
            {
                await AnnLifetimeNativeAssertions.PageAsync(page, target, target.Store.Position);
            }
            else
            {
                await healthy(page, target.Store.Position);
            }
        }
    }

    private static void Set(TestDatabase target, byte[] key, byte[]? value)
        => target.Store.Commit((transaction, _) =>
        {
            if (value is null)
            {
                transaction.Delete(key);
            }
            else
            {
                transaction.Put(key, value);
            }
            return true;
        });
}

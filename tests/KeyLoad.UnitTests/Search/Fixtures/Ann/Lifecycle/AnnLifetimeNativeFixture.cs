using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativeFixture
{
    internal static async Task RunAsync(Func<TestDatabase, TestDatabase, NativeAnnMaintenanceTestRuntime,
        AnnMaintenanceRequest, CommitReceipt, Task> operation, bool derived = false, bool cross = false)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var target = new TestDatabase();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    AnnLifetimeNativeSeed.Configure(source, source: true, derived, cross);
                    AnnLifetimeNativeSeed.Configure(target, source: false, derived, cross);
                    _ = target.Configure(AnnLifetimeNativeProtocol.OtherResource, ResourceKind.Collection);
                    var receipt = AnnLifetimeNativeSeed.Rows(target);
                    if (derived)
                    {
                        receipt = AnnLifetimeNativeDerived.Apply(target, cross);
                    }
                    var pin = NativeAnnMaintenanceTestData.Pin(target);
                    var count = AnnLifetimeNativeProtocol.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var settings = new Dictionary<string, string?>
                    {
                        [AnnLifetimeNativeProtocol.ExactThreshold] = count,
                        [AnnLifetimeNativeProtocol.Breadth] = count,
                        [AnnLifetimeNativeProtocol.EfSearch] = count
                    };
                    await using var runtime = new NativeAnnMaintenanceTestRuntime(target,
                        configuration: new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        var began = await runtime.PhaseAsync(target, pin, AnnMaintenanceCapabilityKind.Begin);
                        _ = await AnnLifetimeNativePublication.FinishAsync(target, runtime, pin, began.Source!.ThroughSequence);
                        await operation(source, target, runtime, pin, receipt);
                    }, failures);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

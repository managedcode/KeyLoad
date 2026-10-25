using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnLifetimeFixture
{
    internal static async Task RunAsync(Func<TestDatabase, TestDatabase, AnnMaintenanceTestRuntime,
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
                    AnnLifetimeSeed.Configure(source, source: true, derived, cross);
                    AnnLifetimeSeed.Configure(target, source: false, derived, cross);
                    _ = target.Configure(AnnLifetimeProtocol.OtherResource, ResourceKind.Collection);
                    var receipt = AnnLifetimeSeed.Rows(target);
                    if (derived)
                    {
                        receipt = AnnLifetimeDerived.Apply(target, cross);
                    }
                    var pin = AnnMaintenanceTestData.Pin(target);
                    var count = AnnLifetimeProtocol.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var settings = new Dictionary<string, string?>
                    {
                        [AnnLifetimeProtocol.ExactThreshold] = count,
                        [AnnLifetimeProtocol.Breadth] = count,
                        [AnnLifetimeProtocol.EfSearch] = count
                    };
                    await using var runtime = new AnnMaintenanceTestRuntime(target,
                        configuration: new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        var began = await runtime.PhaseAsync(target, pin, AnnMaintenanceCapabilityKind.Begin);
                        _ = await AnnLifetimePublication.FinishAsync(target, runtime, pin, began.Source!.ThroughSequence);
                        await operation(source, target, runtime, pin, receipt);
                    }, failures);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundCoreOwner
{
    internal static async Task RunAsync(Func<TestDatabase, NativeRequestWorkOwner, CancellationToken, Task> body,
        CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        await AnalyticsBoundsOwner.SeedAsync(fixture, [D.Event(fixture, D.First, D.Alpha, 2) with { Links = [] }]);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
            var actual = fixture.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)?.DefaultShard)
                ?? throw new InvalidOperationException("The actual analytics physical owner is absent.");
            owner.BindPhysicalOwner(actual);
            owner.AttachResourceLifetimeWorkFence(fixture.Database, fixture.ReadJournalCut().LastIndex);
            await ServerFailureObserver.ObserveAsync(() => body(fixture, owner, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task LeaseAsync(TestDatabase fixture, NativeRequestWorkOwner owner,
        Func<NativeRequestWorkLease, ResourceLifetimeWorkResultRetention, ReadExecutionBudget, Task> body,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        var ledger = new ResourceLifetimeWorkResultRetention(fixture.Database.OperationLimitsOptions);
        var lease = owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            lease.AttachResultRetention(ledger);
            var work = new ReadExecutionBudget(fixture.Database.OperationLimitsOptions, fixture.Database.EvaluationClock, token);
            work.AttachResourceLifetimeBinding(lease);
            await body(lease, ledger, work);
        }, failures);
        await FinishAsync(lease, ledger, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task FinishAsync(NativeRequestWorkLease lease,
        ResourceLifetimeWorkResultRetention ledger, List<Exception> failures)
    {
        var primary = failures.FirstOrDefault();
        var cleanup = new List<Exception>();
        var prepared = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await lease.PrepareCompletionAsync();
            prepared = true;
        }, cleanup);
        if (prepared)
        {
            ServerFailureObserver.Observe(ledger.Dispose, cleanup);
            var cleanupFailure = cleanup.Count switch
            {
                0 => null,
                1 => cleanup[0],
                _ => new AggregateException(cleanup)
            };
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var settled = await lease.CompleteAsync(primary, cleanupFailure);
                if (settled.CleanupOrJoinFailure is { } failure && !cleanup.Contains(failure))
                {
                    throw failure;
                }
            }, cleanup);
        }
        failures.AddRange(cleanup);
    }
}

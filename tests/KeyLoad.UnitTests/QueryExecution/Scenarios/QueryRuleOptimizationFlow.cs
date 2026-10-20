using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryRuleOptimizationFlow
{
    private const long WithdrawnPolicyEpoch = 2L;
    private const long RepairedPolicyEpoch = 3L;

    internal static async Task RunAsync(bool includeNull, bool includeMissing, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new QueryRuleOptimizationFixture(includeNull, includeMissing);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                var db = fixture.Harness;
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, db.Database, db.Store, includeNull, includeMissing, false, token), failures);
                await ServerFailureObserver.ObserveAsync(() => fixture.PolicyAsync(WithdrawnPolicyEpoch, false), failures);
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, db.Database, db.Store, includeNull, includeMissing, true, token), failures);
                await ServerFailureObserver.ObserveAsync(() => fixture.PolicyAsync(RepairedPolicyEpoch, true), failures);
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, db.Database, db.Store, includeNull, includeMissing, false, token), failures);
                await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationCold.RunAsync(fixture, includeNull, includeMissing, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task WaveAsync(QueryRuleOptimizationFixture fixture, DatabaseEngine owner, ZoneTreeStore store,
        bool includeNull, bool includeMissing, bool denied, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationCancellation.RunAsync(fixture, owner, store), failures);
        await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationPages.RunAsync(fixture, owner, store, includeNull, includeMissing, denied, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ObserveAsync(store,
            () => QueryRuleOptimizationCurrent.CheckAsync(fixture, owner, store)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

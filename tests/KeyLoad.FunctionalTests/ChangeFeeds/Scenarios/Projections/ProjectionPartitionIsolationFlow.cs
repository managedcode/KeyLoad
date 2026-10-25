using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class ProjectionPartitionIsolationFlow
{
    private const string Missing = "The complete partition-isolation flow must retain its original continuation.";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(timeProvider: new FeedAuthorityClock());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = await ProjectionPartitionIsolationWarm.RunAsync(database, token);
        database.Store.Dispose();
        var healthy = await ColdAsync(database, async cold =>
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await ProjectionConsumerIdentityAssertions.SameAsync(cold, state.Image, state.Position);
            await ProjectionPartitionIsolationAssertions.VerifyAsync(owner, cold, state, token);
            return await ProjectionPartitionIsolationHealthy.RunAsync(owner, cold, state, token);
        });
        await ColdAsync(database, async cold =>
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await ProjectionConsumerIdentityAssertions.SameAsync(cold, healthy.Image, healthy.Position);
            await ProjectionPartitionIsolationAssertions.VerifyAsync(owner, cold, healthy, token);
            await ProjectionConsumerIdentityAssertions.SameAsync(cold, healthy.Image, healthy.Position);
            return healthy;
        });
    }

    private static async Task<ProjectionPartitionIsolationState> ColdAsync(TestDatabase database,
        Func<ZoneTreeStore, Task<ProjectionPartitionIsolationState>> action)
    {
        var failures = new List<Exception>();
        ProjectionPartitionIsolationState? result = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: database.Database.EvaluationClock);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { result = await action(cold); }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(Missing);
    }
}

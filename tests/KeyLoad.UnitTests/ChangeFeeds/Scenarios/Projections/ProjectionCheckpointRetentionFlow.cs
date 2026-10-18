using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionCheckpointRetentionFlow
{
    private const string MissingResult = "The joined native cold retention operation requires its original continuation.";

    internal static async Task RunAsync(bool ahead)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(timeProvider: new FeedAuthorityClock());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database, ahead), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database, bool ahead)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = await ProjectionCheckpointRetentionFixture.WarmAsync(database, ahead, token);
        database.Store.Dispose();
        var continued = await ColdAsync(database, async cold =>
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await ProjectionCheckpointRetentionFixture.RepairAsync(owner, cold, state, token);
            return await ProjectionCheckpointRetentionSettlement.RunAsync(owner, cold, state, token);
        });
        await ColdAsync(database, async cold =>
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await ProjectionConsumerIdentityAssertions.SameAsync(cold, continued.Image, continued.Position);
            await ProjectionCheckpointRetentionHealthy.AssertAsync(owner, cold, continued, token);
            await ProjectionCheckpointRetentionSettlement.ReplayAsync(owner, cold, continued, token);
            await ProjectionConsumerIdentityAssertions.SameAsync(cold, continued.Image, continued.Position);
            return continued;
        });
    }

    private static async Task<ProjectionCheckpointRetentionContinuation> ColdAsync(TestDatabase database,
        Func<ZoneTreeStore, Task<ProjectionCheckpointRetentionContinuation>> action)
    {
        var failures = new List<Exception>();
        ProjectionCheckpointRetentionContinuation? result = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution(), timeProvider: database.Database.EvaluationClock);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { result = await action(cold); }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingResult);
    }
}

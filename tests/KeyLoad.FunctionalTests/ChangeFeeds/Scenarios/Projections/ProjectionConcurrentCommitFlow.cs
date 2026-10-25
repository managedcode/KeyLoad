using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class ProjectionConcurrentCommitFlow
{
    internal static async Task RunAsync(bool changed)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(timeProvider: new FeedAuthorityClock());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database, changed), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database, bool changed)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = await ProjectionConcurrentCommitFixture.WarmAsync(database, changed, token);
        database.Store.Dispose();
        await ColdAsync(database, state, changed, false, token);
        await ColdAsync(database, state, changed, true, token);
    }

    private static async Task ColdAsync(TestDatabase database, ProjectionConcurrentCommitState state,
        bool changed, bool healthy, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution(), timeProvider: database.Database.EvaluationClock);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var owner = database.CreateConfiguredDatabase(cold);
                await ProjectionConsumerIdentityAssertions.SameAsync(cold, state.Image, state.Position);
                await ProjectionConcurrentCommitAssertions.DocumentsAsync(owner, state.Source, state.WinnerIsFirst, changed, token);
                var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Source.Consumer));
                await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
                    new ProjectionBatch(new(state.Source.Consumer, state.Source.Definition, 3L, false), [], 3L, batch.Token, false));
                await ProjectionConcurrentCommitFixture.ReplayAsync(owner, cold, state.Source, state.First, state.FirstResult,
                    state.Second, state.SecondResult, state.Filtered, state.Advanced, token);
                await ProjectionConsumerIdentityAssertions.SameAsync(cold, state.Image, state.Position);
                if (healthy)
                {
                    await ProjectionConcurrentCommitHealthy.RunAsync(owner, cold, state, token);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

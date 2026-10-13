using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionEmptyTailFlow
{
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
        var state = await ProjectionEmptyTailFixture.WarmAsync(database, token);
        await VerifyAsync(database.Database, database.Store, state, token);
        database.Store.Dispose();
        await ColdAsync(database, state, token);
        await ColdAsync(database, state, token);
    }

    private static async Task VerifyAsync(global::KeyLoad.Core.DatabaseEngine owner, ZoneTreeStore store,
        ProjectionEmptyTailState state, CancellationToken token)
    {
        await Assert.That(ProjectionEmptyTailAssertions.Image(store))
            .IsEquivalentTo(state.Image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(state.Position);
        await ProjectionEmptyTailAssertions.DocumentsAsync(owner, state.Partition, true);
        await ProjectionEmptyTailAssertions.ConsumerAsync(owner, store, state.Consumer, state.Definition, 2L, 2L);
        var current = owner.ReadProjectionBatch(ProjectionEmptyTailAssertions.Root, new(state.Consumer));
        await ProjectionEmptyTailAssertions.ExactAsync(current,
            new ProjectionBatch(new(state.Consumer, state.Definition, 2L, false), [], 2L, current.Token, false));
        await ProjectionEmptyTailAssertions.ReplayAsync(owner, store, state.Empty, state.EmptyResult, token);
        await ProjectionEmptyTailAssertions.ReplayAsync(owner, store, state.Source, state.SourceResult, token);
        await ProjectionEmptyTailAssertions.ReplayAsync(owner, store, state.Rejected, state.Rejection, token);
        await ProjectionEmptyTailAssertions.ReplayAsync(owner, store, state.Complete, state.Completion, token);
        await ProjectionEmptyTailAssertions.ReplayAsync(owner, store, state.Filtered, state.Advancement, token);
    }

    private static async Task ColdAsync(TestDatabase database, ProjectionEmptyTailState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: database.Database.EvaluationClock);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => VerifyAsync(database.CreateConfiguredDatabase(cold), cold, state, token), failures);
        }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

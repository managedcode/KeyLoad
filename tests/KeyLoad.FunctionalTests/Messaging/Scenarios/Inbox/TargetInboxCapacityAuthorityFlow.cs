using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class TargetInboxCapacityAuthorityFlow
{
    internal static async Task RunAsync(int fault)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(source, fault), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase source, int fault)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = TargetInboxCapacityAuthorityFixture.Seed(source, token);
        await TargetInboxAssertions.CapacityAsync(source.Store, state.Original,
            TargetInboxCapacityAuthorityFixture.SecondGeneration);
        var original = QueueWholeFlowStorage.Bytes(source.Store);
        var position = source.Store.Position;
        TargetInboxCapacityAuthorityFixture.Change(source.Store, state, fault, repair: false);
        var damaged = QueueWholeFlowStorage.Bytes(source.Store);
        var duplicate = state.Original with
        {
            CommandId = Guid.NewGuid()
        };
        await TargetInboxCapacityAuthorityAssertions.RefuseAsync(source.Database, source.Store, duplicate, token);
        await TargetInboxCapacityAuthorityAssertions.RefuseAsync(source.Database, source.Store, state.Healthy, token);
        await TargetInboxCapacityAuthorityAssertions.RefuseAsync(source.Database, source.Store, state.Healthy, token);
        await TargetInboxCapacityAuthorityAssertions.HistoryAsync(source.Database, source.Store, state, token);
        await TargetInboxCapacityAuthorityAssertions.SourceAsync(source.Database, state);
        source.JoinOwnedResources();
        var continued = await TargetInboxCapacityAuthorityCold.RunAsync(source, async (database, store) =>
        {
            await TargetInboxCapacityAuthorityAssertions.SameAsync(store, damaged, position + 1L);
            await TargetInboxCapacityAuthorityAssertions.RefuseAsync(database, store, duplicate, token);
            await TargetInboxCapacityAuthorityAssertions.RefuseAsync(database, store, state.Healthy, token);
            await TargetInboxCapacityAuthorityAssertions.HistoryAsync(database, store, state, token);
            await TargetInboxCapacityAuthorityAssertions.SourceAsync(database, state);
            TargetInboxCapacityAuthorityFixture.Change(store, state, fault, repair: true);
            await TargetInboxCapacityAuthorityAssertions.SameAsync(store, original, position + 2L);
            return await TargetInboxCapacityAuthorityContinuationFlow.RunAsync(database, store, state, duplicate, token);
        });
        await TargetInboxCapacityAuthorityCold.RunAsync(source, async (database, store) =>
        {
            await TargetInboxCapacityAuthorityAssertions.SameAsync(store, continued.Image, continued.Position);
            await TargetInboxCapacityAuthorityHealthy.RequireAsync(database, store, state,
                continued.Healthy.Get<CommitInboxResult>(), continued.HealthyPosition);
            await TargetInboxCapacityAuthorityAssertions.HistoryAsync(database, store, state, token);
            await TargetInboxCapacityAuthorityAssertions.ReplayAsync(database, store, state.Healthy, continued.Healthy, token);
            await TargetInboxCapacityAuthorityAssertions.ReplayAsync(database, store, continued.Duplicate, continued.DuplicateResult, token);
            await TargetInboxCapacityAuthorityContinuationFlow.QuotaReplayAsync(database, store, continued.Quota, continued.QuotaResult, token);
            await TargetInboxCapacityAuthorityCompletion.RunAsync(database, store, state, token);
            return continued;
        });
    }
}

using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class FeedDeleteClassificationFlow
{
    private const string MissingResult = "The joined native deletion cold flow must retain its complete continuation.";

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
        var state = await FeedDeleteClassificationFixture.SeedAsync(database, token);
        database.Store.Dispose();
        var continued = await ColdAsync(database, cold => RepairAsync(database, cold, state, token));
        await ColdAsync(database, async cold =>
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await FeedVertexAuthorityAssertions.SameImageAsync(cold, continued.Image, continued.Position);
            await FeedDeleteClassificationAssertions.HealthyAsync(owner, cold, state, continued.Restored, token);
            await FeedDeleteClassificationAssertions.ReplayAsync(owner, cold, state, continued.Resurrection, continued.Restored, token);
            await FeedVertexAuthorityAssertions.SameImageAsync(cold, continued.Image, continued.Position);
            return continued;
        });
    }

    private static async Task<FeedDeleteClassificationContinuation> RepairAsync(TestDatabase database,
        ZoneTreeStore cold, FeedDeleteClassificationState state, CancellationToken token)
    {
        var owner = database.CreateConfiguredDatabase(cold);
        await FeedVertexAuthorityAssertions.SameImageAsync(cold, state.DirtyImage, state.DirtyPosition);
        await FeedDeleteClassificationAssertions.RefuseAsync(owner, cold, state, token);
        cold.Commit((transaction, _) => { transaction.Put(state.Key, state.Original); return true; });
        var prefix = Convert.ToHexString(state.Key) + ":";
        var repaired = state.DirtyImage.Select(row => row.StartsWith(prefix, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(state.Original) : row).ToArray();
        await FeedVertexAuthorityAssertions.SameImageAsync(cold, repaired, state.DirtyPosition + 1L);
        await FeedDeleteClassificationAssertions.HealthyAsync(owner, cold, state, null, token);
        await FeedDeleteClassificationAssertions.ReplayAsync(owner, cold, state, null, null, token);
        var command = new CommandRequest(Guid.NewGuid(), state.Source.Seed.Partition,
            [new PutDocument(FeedVertexAuthorityFixture.Collection, FeedDeleteClassificationFixture.Id,
                FeedDeleteClassificationFixture.RestoredJson, 3L, ExplicitReplacement: true)]);
        token.ThrowIfCancellationRequested();
        var result = FeedNativeHealthyAdmission.Submit(owner,
            owner.CreateCommand(OperationKind.Batch, command.CommandId, FeedVertexAuthorityFixture.Root,
                state.Source.CommittedAt, NativeSerialization.Serialize(command)),
            explicitTime: true, token);
        var receipt = result.Get<CommitReceipt>();
        await FeedVertexAuthorityAssertions.ReceiptAsync(command, receipt,
            new MutationReceipt(FeedVertexAuthorityFixture.PutKind, FeedVertexAuthorityFixture.Collection, FeedDeleteClassificationFixture.Id, 4L));
        await FeedDeleteClassificationAssertions.HealthyAsync(owner, cold, state, receipt, token);
        await FeedDeleteClassificationAssertions.ReplayAsync(owner, cold, state, command, receipt, token);
        return new(state, command, receipt, FeedVertexAuthorityAssertions.Image(cold), cold.Position);
    }

    private static async Task<FeedDeleteClassificationContinuation> ColdAsync(TestDatabase database,
        Func<ZoneTreeStore, Task<FeedDeleteClassificationContinuation>> action)
    {
        var failures = new List<Exception>();
        FeedDeleteClassificationContinuation? result = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution(), timeProvider: database.Database.EvaluationClock);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { result = await action(cold); }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingResult);
    }
}

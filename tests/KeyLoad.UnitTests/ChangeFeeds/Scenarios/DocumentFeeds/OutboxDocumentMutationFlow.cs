using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class OutboxDocumentMutationFlow
{
    private const string MissingResult = "The execution projection cold flow must retain its complete original continuation.";

    internal static async Task RunAsync(int fault)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(timeProvider: new FeedAuthorityClock());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database, fault), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database, int fault)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = await OutboxDocumentMutationFixture.SeedAsync(database, token);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(database.Database, state);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(database.Database, database.Store, state.Seed, state.Seeded, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(database.Database, database.Store, state.Configure, state.Configured, token);
        OutboxDocumentMutationFixture.Damage(database, state, fault);
        await OutboxDocumentMutationAssertions.RefuseAsync(database.Database, database.Store, state, token);
        var image = ProjectionConsumerIdentityAssertions.Image(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        var continued = await ColdAsync(database, cold => RepairAsync(database, cold, state, image, position, token));
        await ColdAsync(database, async cold =>
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await ProjectionConsumerIdentityAssertions.SameAsync(cold, continued.Image, continued.Position);
            await ProjectionConsumerIdentityAssertions.EffectAsync(owner, state);
            await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, state);
            var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Consumer));
            await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
                new ProjectionBatch(new(state.Consumer, state.Definition, 3L, false), [], 3L, batch.Token, false));
            await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, state.Seed, state.Seeded, token);
            await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, state.Complete, continued.Completed, token);
            await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, continued.Filtered, continued.Advanced, token);
            return continued;
        });
    }

    private static async Task<ProjectionConsumerIdentityContinuation> RepairAsync(TestDatabase database, ZoneTreeStore cold,
        ProjectionConsumerIdentityState state, string[] image, long position, CancellationToken token)
    {
        var owner = database.CreateConfiguredDatabase(cold);
        await ProjectionConsumerIdentityAssertions.SameAsync(cold, image, position);
        await OutboxDocumentMutationAssertions.RefuseAsync(owner, cold, state, token);
        cold.Commit((transaction, _) => { transaction.Put(state.Key, state.Original); return true; });
        var prefix = Convert.ToHexString(state.Key) + ":";
        var repaired = image.Select(row => row.StartsWith(prefix, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(state.Original) : row).ToArray();
        await ProjectionConsumerIdentityAssertions.SameAsync(cold, repaired, position + 1L);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(owner, state);
        var completed = owner.ApplyEmbedded(state.Complete, token);
        var result = completed.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(state.Complete, result.Receipt,
            new MutationReceipt("putDocument", ProjectionConsumerIdentityFixture.Target, "effect", 1L));
        await Assert.That(result.AlreadyProcessed).IsFalse();
        await Assert.That(result.Checkpoint).IsEqualTo(2L);
        await ProjectionConsumerIdentityAssertions.EffectAsync(owner, state);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, state.Complete, completed, token);
        var filteredBatch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(filteredBatch,
            new ProjectionBatch(new(state.Consumer, state.Definition, 2L, false), [], 3L, filteredBatch.Token, false));
        var id = Guid.NewGuid();
        var filtered = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.CommitProjectionBatch, id,
            new CommitProjectionBatchRequest(id, state.Consumer, filteredBatch.Token, []));
        var advanced = owner.ApplyEmbedded(filtered, token);
        var settled = advanced.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(filtered, settled.Receipt);
        await Assert.That(settled.AlreadyProcessed).IsFalse();
        await Assert.That(settled.Checkpoint).IsEqualTo(3L);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, filtered, advanced, token);
        return new(completed, filtered, advanced, ProjectionConsumerIdentityAssertions.Image(cold), cold.Position);
    }

    private static async Task<ProjectionConsumerIdentityContinuation> ColdAsync(TestDatabase database,
        Func<ZoneTreeStore, Task<ProjectionConsumerIdentityContinuation>> action)
    {
        ProjectionConsumerIdentityContinuation? result = null;
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: database.Database.EvaluationClock);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { result = await action(cold); }, failures);
        }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingResult);
    }
}

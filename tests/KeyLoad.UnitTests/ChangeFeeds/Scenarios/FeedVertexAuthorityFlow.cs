using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.ChangeFeeds;

internal static class FeedVertexAuthorityFlow
{
    private const string MissingColdResult = "The completed execution cold operation must retain its original result.";

    internal static async Task RunAsync(int? historicalFault = null)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(timeProvider: new FeedAuthorityClock());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database, historicalFault), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database, int? historicalFault)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = await FeedVertexAuthorityFixture.SeedAsync(database, historical: historicalFault.HasValue);
        await FeedVertexAuthorityAssertions.ReceiptAsync(state.Seed, state.Receipt,
            new(FeedVertexAuthorityFixture.PutKind, FeedVertexAuthorityFixture.Collection, "a", 1L),
            new(FeedVertexAuthorityFixture.PutKind, FeedVertexAuthorityFixture.Collection, "b", 1L));
        await FeedVertexAuthorityAssertions.HealthyAsync(database.Database, database.Store, state, null, token);
        if (state.Update is { } warmUpdate && state.UpdateReceipt is { } warmUpdateReceipt)
        {
            await FeedVertexAuthorityAssertions.ReceiptAsync(warmUpdate, warmUpdateReceipt,
                new MutationReceipt(FeedVertexAuthorityFixture.PutKind, FeedVertexAuthorityFixture.Collection, "b", 2L));
        }
        FeedVertexAuthorityFixture.Damage(database, state, historicalFault);
        await RefuseAsync(database.Database, database.Store, state, historicalFault, token);
        var corruptImage = FeedVertexAuthorityAssertions.Image(database.Store);
        var corruptPosition = database.Store.Position;
        database.Store.Dispose();
        var continued = await ColdAsync(database,
            cold => FirstColdAsync(database, cold, state, corruptImage, corruptPosition, historicalFault, token));
        await ColdAsync(database, async secondCold =>
        {
            var owner = database.CreateConfiguredDatabase(secondCold);
            await FeedVertexAuthorityAssertions.SameImageAsync(secondCold, continued.Image, continued.Position);
            await FeedVertexAuthorityAssertions.HealthyAsync(owner, secondCold, state, continued.Receipt, token);
            await FeedVertexAuthorityAssertions.ReplayAsync(owner, secondCold, state.Seed, state.Receipt, state.CommittedAt, token);
            await FeedVertexAuthorityAssertions.ReplayAsync(owner, secondCold, continued.Command, continued.Receipt, state.CommittedAt, token);
            if (state.Update is { } coldUpdate && state.UpdateReceipt is { } coldUpdateReceipt)
            {
                await FeedVertexAuthorityAssertions.ReplayAsync(owner, secondCold, coldUpdate, coldUpdateReceipt, state.CommittedAt, token);
            }
            return continued;
        });
    }

    private static async Task<FeedVertexAuthorityContinuation> FirstColdAsync(TestDatabase database,
        ZoneTreeStore cold, FeedVertexAuthorityState state, string[] corruptImage, long corruptPosition, int? historicalFault, CancellationToken token)
    {
        var owner = database.CreateConfiguredDatabase(cold);
        await FeedVertexAuthorityAssertions.SameImageAsync(cold, corruptImage, corruptPosition);
        await RefuseAsync(owner, cold, state, historicalFault, token);
        cold.Commit((transaction, _) => { transaction.Put(state.Key, state.Original); return true; });
        var prefix = Convert.ToHexString(state.Key) + ":";
        var repaired = corruptImage.Select(row => row.StartsWith(prefix, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(state.Original) : row).ToArray();
        await FeedVertexAuthorityAssertions.SameImageAsync(cold, repaired, corruptPosition + 1L);
        await FeedVertexAuthorityAssertions.HealthyAsync(owner, cold, state, null, token);
        await FeedVertexAuthorityAssertions.ReplayAsync(owner, cold, state.Seed, state.Receipt, state.CommittedAt, token);
        if (state.Update is { } update && state.UpdateReceipt is { } updateReceipt)
        {
            await FeedVertexAuthorityAssertions.ReplayAsync(owner, cold, update, updateReceipt, state.CommittedAt, token);
        }
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(FeedVertexAuthorityFixture.Collection, "c", "{\"n\":3}", 0L)]);
        token.ThrowIfCancellationRequested();
        var receipt = owner.Apply(owner.CreateCommand(OperationKind.Batch, command.CommandId, FeedVertexAuthorityFixture.Root, state.CommittedAt, global::System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options))).Get<CommitReceipt>();
        await FeedVertexAuthorityAssertions.ReceiptAsync(command, receipt,
            new MutationReceipt(FeedVertexAuthorityFixture.PutKind, FeedVertexAuthorityFixture.Collection, "c", 1L));
        await FeedVertexAuthorityAssertions.HealthyAsync(owner, cold, state, receipt, token);
        await FeedVertexAuthorityAssertions.ReplayAsync(owner, cold, command, receipt, state.CommittedAt, token);
        return new(command, receipt, FeedVertexAuthorityAssertions.Image(cold), cold.Position);
    }

    private const string RevisionChainCorruption = "The committed document mutation does not match its outbox receipt.";

    private static Task RefuseAsync(global::KeyLoad.Core.DatabaseEngine owner, ZoneTreeStore store, FeedVertexAuthorityState state,
        int? historicalFault, CancellationToken token)
        => historicalFault is >= 4
            ? FeedVertexAuthorityAssertions.RefuseAsync(owner, store, state, RevisionChainCorruption, token)
            : FeedVertexAuthorityAssertions.RefuseAsync(owner, store, state, token);

    private static async Task<FeedVertexAuthorityContinuation> ColdAsync(TestDatabase database,
        Func<ZoneTreeStore, Task<FeedVertexAuthorityContinuation>> action)
    {
        FeedVertexAuthorityContinuation? result = null;
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
        return result ?? throw new InvalidOperationException(MissingColdResult);
    }
}

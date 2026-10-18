using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class OutboxHeadAuthorityFlow
{
    private const string MissingResult = "The completed native cold flow must retain its original result.";

    internal static async Task RunAsync(int fault)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(limits: new()
            {
                MaxOutboxRecords = 2
            }, timeProvider: new FeedAuthorityClock());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database, fault), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database, int fault)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = await OutboxHeadAuthorityFixture.SeedAsync(database, token);
        await OutboxHeadAuthorityAssertions.ReplayAsync(database.Database, database.Store, state.Seed, state.SeedResult, token);
        await OutboxHeadAuthorityAssertions.ReplayAsync(database.Database, database.Store, state.QuotaWrite, state.QuotaFailure, token);
        OutboxHeadAuthorityFixture.Damage(database, state, fault);
        await OutboxHeadAuthorityAssertions.RefuseAsync(database.Database, database.Store, state, fault, token);
        await OutboxHeadAuthorityAssertions.DocumentsAsync(database.Database, state.Partition, false);
        var image = OutboxHeadAuthorityAssertions.Image(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        var continued = await ColdAsync(database, cold => RepairAsync(database, cold, state, fault, image, position, token));
        await ColdAsync(database, async cold =>
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await OutboxHeadAuthorityAssertions.SameAsync(cold, continued.Image, continued.Position);
            await OutboxHeadAuthorityAssertions.DocumentsAsync(owner, state.Partition, true);
            var entry = cold.Read(view => view.ReadOwnedValue(KeySpace.Partition(OutboxHeadAuthorityFixture.EntrySpace, state.Partition, 3L)))
                ?? throw new InvalidOperationException(OutboxHeadAuthorityFixture.MissingRow);
            await OutboxHeadAuthorityAssertions.ExactAsync(owner.GetOutboxStatus(OutboxHeadAuthorityFixture.Root, state.Partition).Head,
                new OutboxHead(3L, 3L, 1L, entry.LongLength));
            await OutboxHeadAuthorityAssertions.ReplayAsync(owner, cold, state.Seed, state.SeedResult, token);
            await OutboxHeadAuthorityAssertions.ReplayAsync(owner, cold, state.QuotaWrite, state.QuotaFailure, token);
            await OutboxHeadAuthorityAssertions.ReplayAsync(owner, cold, state.Purge, continued.Purged, token);
            await OutboxHeadAuthorityAssertions.ReplayAsync(owner, cold, continued.Healthy, continued.HealthyResult, token);
            return continued;
        });
    }

    private static async Task<OutboxHeadAuthorityContinuation> RepairAsync(TestDatabase database, ZoneTreeStore cold,
        OutboxHeadAuthorityState state, int fault, string[] image, long position, CancellationToken token)
    {
        var owner = database.CreateConfiguredDatabase(cold);
        await OutboxHeadAuthorityAssertions.SameAsync(cold, image, position);
        await OutboxHeadAuthorityAssertions.RefuseAsync(owner, cold, state, fault, token);
        cold.Commit((transaction, _) => { transaction.Put(state.Key, state.Original); return true; });
        var prefix = Convert.ToHexString(state.Key) + ":";
        var repaired = image.Select(row => row.StartsWith(prefix, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(state.Original) : row).ToArray();
        await OutboxHeadAuthorityAssertions.SameAsync(cold, repaired, position + 1L);
        await OutboxHeadAuthorityAssertions.ExactAsync(owner.GetOutboxStatus(OutboxHeadAuthorityFixture.Root, state.Partition).Head,
            new OutboxHead(2L, 1L, 2L, state.StoredBytes));
        var purged = owner.ApplyEmbedded(state.Purge, token);
        await OutboxHeadAuthorityAssertions.ExactAsync(purged.Get<OutboxHead>(), new OutboxHead(2L, 3L, 0L, 0L));
        await OutboxHeadAuthorityAssertions.DocumentsAsync(owner, state.Partition, false);
        await Assert.That(cold.Read(view => view.ReadOwnedValue(KeySpace.Partition(OutboxHeadAuthorityFixture.EntrySpace, state.Partition, 1L)))).IsNull();
        await Assert.That(cold.Read(view => view.ReadOwnedValue(KeySpace.Partition(OutboxHeadAuthorityFixture.EntrySpace, state.Partition, 2L)))).IsNull();
        await OutboxHeadAuthorityAssertions.ReplayAsync(owner, cold, state.QuotaWrite, state.QuotaFailure, token);
        await OutboxHeadAuthorityAssertions.ReplayAsync(owner, cold, state.Purge, purged, token);
        var healthy = OutboxHeadAuthorityFixture.Write(owner, state.Partition, "c");
        var result = owner.ApplyEmbedded(healthy, token);
        var receipt = result.Get<CommitReceipt>();
        await OutboxHeadAuthorityAssertions.ReceiptAsync(healthy, receipt, new MutationReceipt("putDocument", OutboxHeadAuthorityFixture.Collection, "c", 1L));
        await OutboxHeadAuthorityAssertions.DocumentsAsync(owner, state.Partition, true);
        await OutboxHeadAuthorityAssertions.ReplayAsync(owner, cold, healthy, result, token);
        return new(healthy, receipt, result, purged, OutboxHeadAuthorityAssertions.Image(cold), cold.Position);
    }

    private static async Task<OutboxHeadAuthorityContinuation> ColdAsync(TestDatabase database,
        Func<ZoneTreeStore, Task<OutboxHeadAuthorityContinuation>> action)
    {
        OutboxHeadAuthorityContinuation? result = null;
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

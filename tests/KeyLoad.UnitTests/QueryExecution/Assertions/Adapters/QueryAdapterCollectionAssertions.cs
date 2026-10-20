using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterCollectionAssertions
{
    private const string Unsupported = "This expression is outside the supported Q1 C# subset.";
    internal static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var constant = new QueryAdapterUnsafeConstant();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var refused in QueryAdapterCollectionRequests.Refusals(partition, constant))
            {
                await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
                {
                    var before = store.GetReadDiagnostics();
                    var failure = Assert.ThrowsExactly<KeyLoadException>(refused);
                    await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
                    await Assert.That(failure.Message).IsEqualTo(Unsupported);
                    await Assert.That(constant.Calls).IsEqualTo(0);
                    await Assert.That(store.GetReadDiagnostics()).IsEqualTo(before);
                });
                await HealthyAsync(engine, store, partition);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => QueryAdapterNullParityReplay.RequireAsync(database, store, originals), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task HealthyAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition)
    {
        var expected = new QueryPage([new("a", 1, QueryAdapterCollectionFixture.First, false, [])],
            null, store.Position, "bounded-full-scan");
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.Execute("root", new(partition, QueryAdapterCollectionRequests.Sql, AllowFullScan: true)), expected));
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.ExecuteAst("root", QueryAdapterCollectionRequests.Json(partition, false)), expected));
        var query = QueryAdapterCollectionRequests.Base(partition);
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.ExecuteAst("root", query.Where(row => row.Status == "open").ToRequest(true)), expected));
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.ExecuteAst("root", query.Where(row => row.Slots[0] == 2m).ToRequest(true)), expected));
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.ExecuteAst("root", QueryAdapterCollectionRequests.Json(partition, true)), expected));
    }
}

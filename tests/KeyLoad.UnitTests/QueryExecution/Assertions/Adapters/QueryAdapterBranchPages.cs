using KeyLoad.Client;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryAdapterBranchPages
{
    private const string Principal = "root";
    private const string Collection = "orders";
    private const string AccessPath = "bounded-full-scan";
    private const long Revision = 1;
    private const int SecondPage = 2;
    private const decimal BranchBoundary = 3m;
    private const string AJson = "{\"number\":2,\"status\":\"open\"}";
    private const string BJson = "{\"number\":4,\"status\":\"closed\"}";
    private const string CJson = "{\"number\":1,\"status\":\"hold\"}";

    internal static async Task RunAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition)
    {
        var original = KeyLoadQuery.From<QueryAdapterOrder>(partition, Collection, UnitClientOptions.Translation());
        var a = original.Where(row => row.Number > BranchBoundary).OrderBy(row => row.Number).Take(1);
        var b = original.Where(row => row.Status == "open").OrderByDescending(row => row.Status).Take(SecondPage);
        var first = new QueryPage([new("b", Revision, BJson, false, [])], null, store.Position, AccessPath);
        var second = new QueryPage([new("a", Revision, AJson, false, [])], null, store.Position, AccessPath);
        var whole = new QueryPage([new("a", Revision, AJson, false, []), new("b", Revision, BJson, false, []),
            new("c", Revision, CJson, false, [])], null, store.Position, AccessPath);
        await ReadAsync(store, () => engine.ExecuteAst(Principal, QueryAdapterTestSupport.RoundTrip(a.ToRequest(true))), first);
        await ReadAsync(store, () => engine.ExecuteAst(Principal, QueryAdapterTestSupport.RoundTrip(b.ToRequest(true))), second);
        await ReadAsync(store, () => engine.Execute(Principal, new(partition,
            "SELECT TOP (1) * FROM orders WHERE number > 3 ORDER BY number", AllowFullScan: true)), first);
        await ReadAsync(store, () => engine.Execute(Principal, new(partition,
            "SELECT TOP (2) * FROM orders WHERE status = 'open' ORDER BY status DESC", AllowFullScan: true)), second);
        await RefusalsAsync(engine, store, original, a);
        await ReadAsync(store, () => engine.ExecuteAst(Principal, QueryAdapterTestSupport.RoundTrip(b.ToRequest(true))), second);
        await ReadAsync(store, () => engine.ExecuteAst(Principal, QueryAdapterTestSupport.RoundTrip(
            original.OrderBy(row => QueryFunctions.DocumentId(row)).ToRequest(true))), whole);
        await ReadAsync(store, () => engine.Execute(Principal, new(partition,
            "SELECT * FROM orders ORDER BY id", AllowFullScan: true)), whole);
    }

    private static async Task RefusalsAsync(QueryEngine engine, ZoneTreeStore store,
        KeyLoadQuery<QueryAdapterOrder> original, KeyLoadQuery<QueryAdapterOrder> branch)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                original.Where(row => row.Status.StartsWith('o'))).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.ExecuteAst(Principal,
                QueryAdapterTestSupport.RoundTrip(branch.ToRequest(true)), cancellationToken: cancelled.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ReadAsync(ZoneTreeStore store, Func<QueryPage> read, QueryPage expected)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await SqlOffsetPages.ExactAsync(read(), expected), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryAdapterNullParityPages
{
    internal static async Task HealthyAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition, string principal)
    {
        var expected = QueryAdapterNullParityProtocol.Expected(store.Position);
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.Execute(principal, new(partition, QueryAdapterNullParityProtocol.Sql, AllowFullScan: true)), expected));
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.ExecuteAst(principal, QueryAdapterNullParityProtocol.Json(partition)), expected));
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.ExecuteAst(principal, QueryAdapterNullParityProtocol.CSharp(partition)), expected));
        await QueryAdapterNullParityConservation.ObserveAsync(store, () => SqlOffsetPages.ExactAsync(
            engine.ExecuteAst(principal, QueryAdapterNullParityProtocol.CSharp(partition, true)),
            QueryAdapterNullParityProtocol.Expected(store.Position, true)));
    }
    internal static async Task RefusalsAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition)
    {
        await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(QueryAdapterNullParityProtocol.Root,
                new(partition, "SELECT * FROM orders WHERE n IS MISSING", AllowFullScan: true)));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        foreach (var route in new[] { 0, 1, 2 })
        {
            await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
            {
                var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
                {
                    _ = route == 0
                        ? engine.Execute(QueryAdapterNullParityProtocol.Root,
                            new(partition, QueryAdapterNullParityProtocol.Sql, AllowFullScan: true), cancellationToken: cancelled.Token)
                        : engine.ExecuteAst(QueryAdapterNullParityProtocol.Root, route == 1
                            ? QueryAdapterNullParityProtocol.Json(partition) : QueryAdapterNullParityProtocol.CSharp(partition),
                            cancellationToken: cancelled.Token);
                });
                await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
            });
        }
        await HealthyAsync(engine, store, partition, QueryAdapterNullParityProtocol.Root);
    }
}

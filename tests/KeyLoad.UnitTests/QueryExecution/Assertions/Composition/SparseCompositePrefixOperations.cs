using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SparseCompositePrefixOperations
{
    internal static async Task RunAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition,
        bool includeNull, bool includeMissing, List<(ReplicatedOperation Operation, byte[] Result)> originals)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
            foreach (var fallback in new[] { false, true })
            {
                await CollectionAsync(engine, store, partition, includeNull, includeMissing, fallback);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ReplayAsync(owner, store, originals), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        List<(ReplicatedOperation Operation, byte[] Result)> originals)
    {
        foreach (var original in originals)
        {
            await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
            {
                var replay = owner.ApplyEmbedded(original.Operation, cancellationToken: default);
                await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(replay)))
                    .IsEqualTo(Convert.ToHexString(original.Result));
                if (original.Operation.Kind == OperationKind.Batch)
                {
                    await SparseCompositePrefixFixture.NoReferencesAsync(replay.Get<CommitReceipt>());
                }
            });
        }
    }
    private static async Task CollectionAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition,
        bool includeNull, bool includeMissing, bool fallback)
    {
        var collection = fallback ? SparseCompositePrefixData.Fallback : SparseCompositePrefixData.Only;
        foreach (var shape in new[] { 0, 1, 2 })
        {
            foreach (var reference in new[] { false, true })
            {
                var path = reference ? SparseCompositePrefixData.Scan
                    : SparseCompositePrefixData.Path(includeNull, includeMissing, fallback, shape);
                await PagesAsync(engine, store, partition, collection, shape, reference, path);
            }
        }
        await ConsentAsync(engine, store, partition, collection, includeNull && includeMissing || fallback,
            SparseCompositePrefixData.Path(includeNull, includeMissing, fallback, 0));
    }
    private static async Task PagesAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition,
        string collection, int shape, bool reference, string path)
    {
        var expected = new QueryPage([.. SparseCompositePrefixData.Expected(shape)], null, store.Position, path);
        foreach (var execute in new Func<QueryPage>[]
        {
            () => engine.Execute(SparseCompositePrefixData.Reader,
                SparseCompositePrefixRequests.Sql(partition, collection, shape, reference)),
            () => engine.ExecuteAst(SparseCompositePrefixData.Reader,
                SparseCompositePrefixRequests.Ast(partition, collection, shape, reference)),
            () => engine.ExecuteAst(SparseCompositePrefixData.Reader,
                SparseCompositePrefixRequests.CSharp(partition, collection, shape, reference))
        })
        {
            await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
                await SqlOffsetPages.ExactAsync(execute(), expected));
        }
    }
    private static async Task ConsentAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition,
        string collection, bool indexed, string path)
    {
        foreach (var execute in new Func<QueryPage>[]
        {
            () => engine.Execute(SparseCompositePrefixData.Reader,
                SparseCompositePrefixRequests.Sql(partition, collection, 0, false, false)),
            () => engine.ExecuteAst(SparseCompositePrefixData.Reader,
                SparseCompositePrefixRequests.Ast(partition, collection, 0, false, false)),
            () => engine.ExecuteAst(SparseCompositePrefixData.Reader,
                SparseCompositePrefixRequests.CSharp(partition, collection, 0, false, false))
        })
        {
            await QueryAdapterNullParityConservation.ObserveAsync(store, async () =>
            {
                if (indexed)
                {
                    var actual = execute();
                    await SqlOffsetPages.ExactAsync(actual, new([.. SparseCompositePrefixData.Expected(0)], null,
                        store.Position, path));
                    return;
                }
                var failed = Assert.ThrowsExactly<KeyLoadException>(() => execute());
                await Assert.That(failed.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
                await Assert.That(failed.Message).IsEqualTo("The query requires an index or explicit full-scan permission.");
            });
        }
    }
}

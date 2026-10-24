using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class ProjectRowAuthorityAssertions
{
    private const string Reader = "alice";
    private const string Sql = "SELECT * FROM orders ORDER BY id";
    private const string ScanPath = "bounded-full-scan";
    private const string Mine = "mine";
    private const string Hidden = "hidden";
    private const string EmptyJson = "{}";
    private const string ExplainId = "explain";
    private const long MineRevision = 1;
    private const long ProjectRevision = 2;
    private const long ExplainRevision = 0;

    internal static async Task ReadAsync(TestDatabase db, QueryEngine engine, ZoneTreeStore store,
        bool projectVisible, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var request = new QueryRequest(db.Partition, Sql, AllowFullScan: true);
        var actual = engine.Execute(Reader, request, cancellationToken: token);
        QueryRow[] rows = projectVisible ? [new(Hidden, ProjectRevision, EmptyJson, false, []),
            new(Mine, MineRevision, EmptyJson, false, [])] : [new(Mine, MineRevision, EmptyJson, false, [])];
        await Assert.That(JsonDefaults.Serialize(actual.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(rows))).IsTrue();
        await PageAsync(actual, cut);
        var plan = JsonSerializer.Serialize(new
        {
            AccessPath = ScanPath,
            AtomicPartition = db.Partition.AtomicPartitionId,
            ScanBudget = UnitExecutionOptions.DatabaseLimits().Value.MaxScanRecords
        }, JsonDefaults.Options);
        var explain = engine.ExecuteAst(Reader, new AstQueryRequest(db.Partition,
            new SqlParser(Sql, UnitExecutionOptions.DatabaseLimits(db.Database.Limits), UnitExecutionOptions.QueryExecution(),
                new global::KeyLoad.Core.ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(db.Database.Limits),
                    cancellationToken: token)).Parse() with
            {
                Explain = true
            }, AllowFullScan: true), cancellationToken: token);
        QueryRow[] explanation = [new(ExplainId, ExplainRevision, plan)];
        await Assert.That(JsonDefaults.Serialize(explain.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(explanation))).IsTrue();
        await PageAsync(explain, cut);
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    internal static async Task RefusedAsync(TestDatabase db, QueryEngine engine, ZoneTreeStore store,
        CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        foreach (var explain in new[] { false, true })
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            {
                _ = explain
                ? engine.ExecuteAst(Reader, new AstQueryRequest(db.Partition,
                    new SqlParser(Sql, UnitExecutionOptions.DatabaseLimits(db.Database.Limits), UnitExecutionOptions.QueryExecution(),
                new global::KeyLoad.Core.ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(db.Database.Limits),
                    cancellationToken: token)).Parse() with
                    {
                        Explain = true
                    }, AllowFullScan: true), cancellationToken: token)
                : engine.Execute(Reader, new(db.Partition, Sql, AllowFullScan: true), cancellationToken: token);
            });
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        }
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    private static async Task PageAsync(QueryPage page, long cut)
    {
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(ScanPath);
    }
}

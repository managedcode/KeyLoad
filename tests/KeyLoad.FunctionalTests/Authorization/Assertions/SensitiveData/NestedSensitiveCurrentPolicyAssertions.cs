using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class NestedSensitiveCurrentPolicyAssertions
{
    private const string PredicateSql = "SELECT * FROM orders d WHERE d.\"odd.name\" = 'CANARY'";
    private const string SortSql = "SELECT * FROM orders ORDER BY \"odd.name\"";
    private const string Scan = "bounded-full-scan";
    private const long CurrentRevision = 2;

    internal static async Task ProtectedAsync(TestDatabase db, QueryEngine queries, ZoneTreeStore store,
        CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        foreach (var sql in new[] { PredicateSql, SortSql })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(
                NestedSensitiveCurrentPolicyContinuation.Reader, new(db.Partition, sql, AllowFullScan: true), cancellationToken: token));
            await ErrorAsync(error, ErrorCode.PermissionDenied);
        }
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    internal static async Task HealthyAsync(TestDatabase db, DatabaseEngine database, QueryEngine queries,
        ZoneTreeStore store, string json, long revision, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var reference = db.ExpectedReference(new(db.Partition, NestedSensitiveCurrentPolicyContinuation.Collection,
            NestedSensitiveCurrentPolicyContinuation.DocumentId));
        var document = database.GetDocument(NestedSensitiveCurrentPolicyContinuation.Reader, reference, cancellationToken: token);
        var expectedDocument = new DocumentResult(reference, revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(document).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedDocument))).IsTrue();
        QueryRow[] expectedRows = [new(NestedSensitiveCurrentPolicyContinuation.DocumentId, revision, json, false, [])];
        foreach (var sql in new[] { PredicateSql, SortSql })
        {
            var page = queries.Execute(NestedSensitiveCurrentPolicyContinuation.Reader,
                new(db.Partition, sql, AllowFullScan: true), cancellationToken: token);
            await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedRows))).IsTrue();
            await Assert.That(page.CutPosition).IsEqualTo(cut);
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.AccessPath).IsEqualTo(Scan);
        }
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    internal static async Task RevokedAsync(TestDatabase db, DatabaseEngine database, QueryEngine queries,
        ZoneTreeStore store, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var reference = db.ExpectedReference(new(db.Partition, NestedSensitiveCurrentPolicyContinuation.Collection,
            NestedSensitiveCurrentPolicyContinuation.DocumentId));
        await ErrorAsync(Assert.ThrowsExactly<KeyLoadException>(() => database.GetDocument(
            NestedSensitiveCurrentPolicyContinuation.Reader, reference, cancellationToken: token)), ErrorCode.Unauthenticated);
        foreach (var sql in new[] { PredicateSql, SortSql })
        {
            await ErrorAsync(Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(
                NestedSensitiveCurrentPolicyContinuation.Reader, new(db.Partition, sql, AllowFullScan: true),
                cancellationToken: token)), ErrorCode.Unauthenticated);
        }
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    private static async Task ErrorAsync(KeyLoadException error, ErrorCode expected)
    {
        await Assert.That(error.Code).IsEqualTo(expected);
        await Assert.That(error.Message).DoesNotContain(NestedSensitiveCurrentPolicyContinuation.Canary);
    }

    internal static async Task CurrentAsync(TestDatabase db, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => await HealthyAsync(db, database, new QueryEngine(database, UnitExecutionOptions.QueryExecution()), store,
            NestedSensitiveCurrentPolicyContinuation.UpdatedJson, CurrentRevision, token);
}

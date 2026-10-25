using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlBetweenAuthorityResults
{
    private const string Sql = "SELECT * FROM orders WHERE number BETWEEN lowerBound AND upperBound";
    private const string RedactedJson = "{\"number\":5}";
    private const string RawJson = "{\"number\":5,\"lowerBound\":1,\"upperBound\":9}";
    private const string LowerPath = "/lowerBound";
    private const string UpperPath = "/upperBound";
    private const string Scan = "bounded-full-scan";
    private const long Revision = 1;

    internal static async Task PageAsync(TestDatabase db, DatabaseEngine database, ZoneTreeStore store, bool raw)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var page = queries.Execute(SqlBetweenAuthorityContinuation.Reader, SqlBetweenTestData.Request(db, Sql));
        QueryRow[] expected = [new(SqlBetweenTestData.RowMiddle, Revision,
            raw ? RawJson : RedactedJson, !raw, raw ? [] : [LowerPath, UpperPath])];
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        var reference = db.ExpectedReference(new(db.Partition, SqlBetweenTestData.Collection, SqlBetweenTestData.RowMiddle));
        var point = database.GetDocument(SqlBetweenAuthorityContinuation.Reader, reference);
        DocumentResult expectedPoint = new(reference, Revision, raw ? RawJson : RedactedJson,
            !raw, raw ? [] : [LowerPath, UpperPath]);
        await Assert.That(JsonDefaults.Serialize(point).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedPoint))).IsTrue();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    internal static async Task RevokedAsync(TestDatabase db, DatabaseEngine database, ZoneTreeStore store)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(
            SqlBetweenAuthorityContinuation.Reader, SqlBetweenTestData.Request(db, Sql)));
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }
}

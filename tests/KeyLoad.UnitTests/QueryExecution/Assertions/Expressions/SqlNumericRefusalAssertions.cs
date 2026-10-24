using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlNumericRefusalAssertions
{
    private const string ScanAccessPath = "bounded-full-scan";
    private const long OriginalRevision = 1;
    private const string High = "{\"number\":9,\"label\":\"violet\",\"flag\":false}";
    private const string Low = "{\"number\":1,\"label\":\"amber\",\"flag\":false}";
    private const string Middle = "{\"number\":5,\"label\":\"moss\",\"flag\":true}";

    internal static async Task HealthyAsync(TestDatabase db, ZoneTreeStore store, QueryEngine engine, string sql)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        var expected = new QueryPage([
            new("high", OriginalRevision, High, false, []),
            new("low", OriginalRevision, Low, false, []),
            new("middle", OriginalRevision, Middle, false, [])], null, cut, ScanAccessPath);
        var actual = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(db, sql));
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await UnchangedAsync(store, image, cut);
    }

    internal static async Task RefusedAsync(TestDatabase db, ZoneTreeStore store, QueryEngine engine, string sql, ErrorCode code)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        QueryPage? partial = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.Execute(
            SqlBetweenTestData.Root, SqlBetweenTestData.Request(db, sql)));
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
        await Assert.That(error.Message).DoesNotContain(High);
        await Assert.That(error.Message).DoesNotContain(Low);
        await Assert.That(error.Message).DoesNotContain(Middle);
        await UnchangedAsync(store, image, cut);
    }

    internal static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long cut)
    {
        await Assert.That(store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }
}

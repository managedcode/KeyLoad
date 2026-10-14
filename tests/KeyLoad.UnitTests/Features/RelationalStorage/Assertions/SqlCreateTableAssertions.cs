using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlCreateTableAssertions
{
    internal static async Task SameAsync<TExpected, TActual>(TExpected expected, TActual actual)
        => await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected),
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    internal static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long cut)
    {
        await Assert.That(store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
    internal static async Task RowsAsync(TestDatabase fixture, ZoneTreeStore store, DatabaseEngine database)
        => await RowsAsync(fixture, store, database, SqlCreateTableLiteral.Selected, SqlCreateTableLiteral.Revision);
    internal static async Task RowsAsync(TestDatabase fixture, ZoneTreeStore store, DatabaseEngine database, string selected, long revision)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var page = engine.Execute(SqlCreateTableLiteral.Root, new(fixture.Partition, SqlCreateTableLiteral.Select, AllowFullScan: true));
        var expected = new QueryRow[] { new(SqlCreateTableLiteral.One, revision, selected, false, []) };
        await PageAsync(page, expected, cut);
        var ast = new global::KeyLoad.Query.AstQueryRequest(fixture.Partition,
            new global::KeyLoad.Query.SelectQuery(SqlCreateTableLiteral.Table, null,
                [new("/title", "title"), new("/amount", "amount"), new("/active", "active")],
                null, [new("/@id", false)], MaximumRows), AllowFullScan: true);
        await PageAsync(engine.ExecuteAst(SqlCreateTableLiteral.Root, ast), expected, cut);
        await UnchangedAsync(store, image, cut);
    }
    private static async Task PageAsync(QueryPage page, QueryRow[] expected, long cut)
    {
        await SameAsync(expected, page.Rows);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(SqlCreateTableLiteral.AccessPath);
    }
    private const int MaximumRows = 5;
}

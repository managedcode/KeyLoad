using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class AccessPathPredicateSemanticsTests
{
    private const string Sql = "SELECT * FROM orders WHERE ";

    [Test]
    [Arguments("4 = 5", "")]
    [Arguments("id = 'b' AND 4 = 5", "")]
    [Arguments("'b' = id AND 4 = 5", "")]
    [Arguments("number = 3 AND 4 = 5", "")]
    [Arguments("number >= 2 AND number <= 4 AND 4 = 5", "")]
    [Arguments("id = 'b' OR 4 = 5", "b")]
    [Arguments("id = 'a' OR id = 'b'", "a,b")]
    [Arguments("number = NULL", "")]
    [Arguments("number = 3 OR number IS NULL", "b,null")]
    [Arguments("number = 0.0", "zero")]
    public async Task AcAisql008AccessSelectionNeverReplacesTheOriginalPredicate(string predicate, string expected)
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("number", ["/number"])]);
        db.Commit(new PutDocument("orders", "a", "{\"number\":2}"), new PutDocument("orders", "b", "{\"number\":3}"),
            new PutDocument("orders", "c", "{\"number\":4}"), new PutDocument("orders", "null", "{\"number\":null}"),
            new PutDocument("orders", "zero", "{\"number\":0}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(db.Partition, Sql + predicate + " ORDER BY id", AllowFullScan: true);
        var cut = db.Store.Position;
        var sql = engine.Execute("root", request);
        var ast = engine.ExecuteAst("root", new(db.Partition,
            new SqlParser(request.Sql, UnitExecutionOptions.QueryExecution()).Parse(), AllowFullScan: true));

        await Assert.That(string.Join(',', sql.Rows.Select(row => row.EntityId))).IsEqualTo(expected);
        await EqualityAccessPathSupport.SameRows(sql, ast);
        await Assert.That(db.Store.Position).IsEqualTo(cut);
        await Assert.That(engine.Execute("root", new(db.Partition, Sql + "id = 'b'")).Rows.Single().EntityId).IsEqualTo("b");
    }
}

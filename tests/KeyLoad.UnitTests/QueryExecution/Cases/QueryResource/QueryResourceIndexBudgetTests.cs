using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryResourceIndexBudgetTests
{
    private const string Collection = "orders";
    private const string IndexName = "status";
    private const string StatusPath = "/status";
    private const string DocumentId = "a";
    private const string PointSql = "SELECT * FROM orders WHERE id = 'a'";
    private const string IndexSql = "SELECT * FROM orders WHERE status = 'open'";
    private const int ReadBudgetBytes = 256;
    private const int PayloadLength = 512;

    [Test]
    public async Task AcMp003PointAndIndexDereferenceConsumeTheSameRawReadBudget()
    {
        using var db = new TestDatabase(new DatabaseLimits { MaxQueryReadBytes = ReadBudgetBytes });
        db.Configure(Collection, ResourceKind.Collection, indexes: [new(IndexName, [StatusPath])]);
        db.Commit(new PutDocument(Collection, DocumentId,
            "{\"status\":\"open\",\"payload\":\"" + new string('x', PayloadLength) + "\"}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var point = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root", new(db.Partition, PointSql)));
        var index = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root", new(db.Partition, IndexSql)));
        await Assert.That(point.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(index.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}

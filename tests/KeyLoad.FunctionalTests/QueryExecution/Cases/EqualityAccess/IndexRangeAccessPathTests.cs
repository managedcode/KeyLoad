using System.Globalization;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class IndexRangeAccessPathTests
{
    private const string Collection = "orders";
    private const string Index = "number";
    private const string Field = "/number";
    private const string Root = "root";
    private const string Reader = "reader";
    private const string Sensitive = "pii";
    private const string Lower = "lower";
    private const string Upper = "upper";
    private const string IndexPath = "index:number";
    private const string Select = "SELECT * FROM orders WHERE ";
    private const string Ordered = " ORDER BY id";
    private const string ParameterSql = "SELECT * FROM orders WHERE @lower <= number AND @upper > number ORDER BY id";
    private const string PageSql = "SELECT TOP 2 * FROM orders WHERE number >= 2 AND number <= 4 ORDER BY id";
    private const int Records = 128;

    [Test]
    [Arguments("number >= 2 AND number <= 4", "d002,d003,d004,duplicate")]
    [Arguments("2 < number AND 4 > number", "d003,duplicate")]
    [Arguments("number > 3 AND number < 4", "")]
    [Arguments("number >= 3 AND number <= 3", "d003,duplicate")]
    [Arguments("number <= 0", "d000")]
    [Arguments("number >= 127", "d127")]
    [Arguments("number > 127", "")]
    [Arguments("number >= 4 AND number < 2", "")]
    [Arguments("number >= 1 AND number >= 2 AND number < 4 AND number < 3", "d002")]
    [Arguments("number >= 2 AND number <= 4 AND number <> 3", "d002,d004")]
    public async Task AcAisql008ScalarRangeSeeksKeepExactBoundsDuplicatesAndResiduals(string predicate, string expected)
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(db.Partition, Select + predicate + Ordered);
        var ast = new SqlParser(request.Sql, UnitExecutionOptions.QueryExecution()).Parse();
        var sql = engine.Execute(Root, request);
        var typed = engine.ExecuteAst(Root, new(db.Partition, ast));

        await Assert.That(sql.AccessPath).IsEqualTo(IndexPath);
        await Assert.That(string.Join(',', sql.Rows.Select(row => row.EntityId))).IsEqualTo(expected);
        await EqualityAccessPathSupport.SameRows(sql, typed);
    }

    [Test]
    public async Task AcAisql008BoundParametersSeekPastEarlierIndexEntries()
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var before = db.Store.GetReadDiagnostics();
        var page = engine.Execute(Root, new(db.Partition, ParameterSql,
            new() { [Lower] = JsonSerializer.SerializeToElement(125), [Upper] = JsonSerializer.SerializeToElement(128) }));
        var after = db.Store.GetReadDiagnostics();

        await Assert.That(page.AccessPath).IsEqualTo(IndexPath);
        await Assert.That(string.Join(',', page.Rows.Select(row => row.EntityId))).IsEqualTo("d125,d126,d127");
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsLessThanOrEqualTo(4);
        await Assert.That(after.RangeStagedEntries - before.RangeStagedEntries).IsEqualTo(0);
    }

    [Test]
    public async Task AcAisql008RangePagesRetainTheCursorAndRejectSourceChanges()
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(db.Partition, PageSql);
        var first = engine.Execute(Root, request);
        var second = engine.Execute(Root, request with { Cursor = first.Cursor });

        await Assert.That(string.Join(',', first.Rows.Select(row => row.EntityId))).IsEqualTo("d002,d003");
        await Assert.That(first.Cursor).IsNotNull();
        await Assert.That(string.Join(',', second.Rows.Select(row => row.EntityId))).IsEqualTo("d004,duplicate");
        await Assert.That(second.Cursor).IsNull();
        db.Commit(new PutDocument(Collection, "d003", "{\"number\":5}", ExplicitReplacement: true));
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Root, request with { Cursor = first.Cursor }));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(string.Join(',', engine.Execute(Root, request).Rows.Select(row => row.EntityId))).IsEqualTo("d002,d004");
    }

    [Test]
    public async Task AcAisql008ProtectedRangeFieldsAreDeniedBeforeTheIndexIsRead()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection, indexes: [new(Index, [Field])], fields: [new(Field, Sensitive)]);
        db.Commit(new PutDocument(Collection, "private", "{\"number\":3}"));
        _ = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, "tenant",
            [new("database", Collection, Capability.DocumentsRead | Capability.Query)], []))).Get<PrincipalRecord>();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var before = db.Store.GetReadDiagnostics();
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader, new(db.Partition, PageSql)));

        await Assert.That(refused.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Store.GetReadDiagnostics().RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        await Assert.That(engine.Execute(Root, new(db.Partition, PageSql)).Rows).HasSingleItem();
    }

    [Test]
    public async Task AcAisql008RangeCandidatesKeepFreshPersistedRowOwnership()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection, indexes: [new(Index, [Field])]);
        db.Commit(new PutDocument(Collection, "owned", "{\"number\":3}", Access: new(Reader)),
            new PutDocument(Collection, "other", "{\"number\":3}", Access: new("other")));
        _ = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, "tenant",
            [new("database", Collection, Capability.DocumentsRead | Capability.Query)], [])
        {
            OwnerId = Reader,
            RestrictRows = true
        })).Get<PrincipalRecord>();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(db.Partition, PageSql);
        await Assert.That(engine.Execute(Reader, request).Rows.Single().EntityId).IsEqualTo("owned");
        _ = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, "tenant", [], [])
        {
            PolicyEpoch = 2
        }))
            .Get<PrincipalRecord>();
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader, request));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(engine.Execute(Root, request).Rows.Length).IsEqualTo(2);
    }

    [Test]
    public async Task AcAisql008DisjunctionCannotChooseOnlyOneRangeAndCancellationLeavesAHealthyRead()
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Root,
            new(db.Partition, Select + "number < 2 OR number > 126")));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _ = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(Root,
            new(db.Partition, PageSql), cancellationToken: cancellation.Token));
        await Assert.That(engine.Execute(Root, new(db.Partition, PageSql)).Rows.Length).IsEqualTo(2);
    }

    private static TestDatabase Create()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection, indexes: [new(Index, [Field])]);
        db.Commit([.. Enumerable.Range(0, Records).Select(number => (Mutation)new PutDocument(Collection,
            "d" + number.ToString("D3", CultureInfo.InvariantCulture),
            string.Create(CultureInfo.InvariantCulture, $"{{\"number\":{number}}}"))),
            new PutDocument(Collection, "duplicate", "{\"number\":3}"),
            new PutDocument(Collection, "null", "{\"number\":null}"), new PutDocument(Collection, "missing", "{}")]);
        return db;
    }
}

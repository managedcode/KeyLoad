using System.Globalization;
using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class PrimaryKeyRangeAccessPathTests
{
    private const string Collection = "orders";
    private const string Root = "root";
    private const string Id = "id";
    private const string Amount = "amount";
    private const string From = "from";
    private const string To = "to";
    private const string AccessPath = "primary-key-range";
    private const string Scan = "primary-key-range-seek";
    private const string ScanProperty = "scan";
    private const string Reader = "reader";
    private const string Other = "other";
    private const string PageSql = "SELECT TOP 1 id FROM orders WHERE id BETWEEN 'd125' AND 'd127' ORDER BY id";
    private const int Records = 128;

    [Test]
    [Arguments("id BETWEEN @from AND @to", "d125,d126,d127")]
    [Arguments("id > @from AND id <= @to", "d126,d127")]
    [Arguments("id >= 'd127' AND id <= 'd125'", "")]
    [Arguments("id >= 'd125' AND id <= 'd125'", "d125")]
    [Arguments("'d125' <= id AND 'd127' >= id", "d125,d126,d127")]
    [Arguments("id >= 'd125' AND id <= 'd127' AND amount <> 126", "d125,d127")]
    [Arguments("id >= 'd127'", "d127")]
    [Arguments("id < 'd001'", "d000")]
    [Arguments("id > 'd127'", "")]
    [Arguments("id >= 'd125' AND id >= 'd126' AND id < 'd127'", "d126")]
    [Arguments("id <= 'd001'", "d000,d001")]
    public async Task AcAisql008PrimaryKeyRangesSeekExactBoundsAndExplainWithoutScanning(string predicate, string expected)
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        Dictionary<string, JsonElement>? parameters = predicate.Contains('@', StringComparison.Ordinal) ? new()
        {
            [From] = JsonSerializer.SerializeToElement("d125"),
            [To] = JsonSerializer.SerializeToElement("d127")
        } : null;
        var request = new QueryRequest(db.Partition, "SELECT id FROM orders WHERE " + predicate + " ORDER BY id", parameters);
        var ast = new SqlParser(request.Sql, UnitExecutionOptions.QueryExecution()).Parse();
        var before = db.Store.GetReadDiagnostics();
        var cut = db.Store.Position;
        using var plan = JsonDocument.Parse(engine.ExecuteAst(Root,
            new(db.Partition, ast with { Explain = true }, parameters)).Rows.Single().Json);
        await Assert.That(plan.RootElement.GetProperty(ScanProperty).GetString()).IsEqualTo(Scan);
        await Assert.That(db.Store.GetReadDiagnostics().RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        var sql = engine.Execute(Root, request);
        var after = db.Store.GetReadDiagnostics();
        var typed = engine.ExecuteAst(Root, new(db.Partition, ast, parameters));

        await Assert.That(sql.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(string.Join(',', sql.Rows.Select(row => row.EntityId))).IsEqualTo(expected);
        await EqualityAccessPathSupport.SameRows(sql, typed);
        var candidates = predicate.Contains("amount", StringComparison.Ordinal) ? 3 : expected.Length == 0 ? 0 : expected.Split(',').Length;
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsLessThanOrEqualTo(candidates + 1);
        await Assert.That(db.Store.Position).IsEqualTo(cut);
        await Assert.That(engine.Execute(Root, new(db.Partition, "SELECT id FROM orders WHERE id = 'd003'"))
            .Rows.Single().EntityId).IsEqualTo("d003");
    }

    [Test]
    public async Task AcAisql008PrimaryRangePagesKeepFreshVisibilityCancellationAndPolicy()
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        _ = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.Query)], [])
        {
            OwnerId = Reader,
            RestrictRows = true
        })).Get<PrincipalRecord>();
        var request = new QueryRequest(db.Partition, PageSql);
        var first = engine.Execute(Reader, request);
        await Assert.That(first.Rows.Single().EntityId).IsEqualTo("d125");
        await Assert.That(first.Cursor).IsNotNull();
        var second = engine.Execute(Reader, request with { Cursor = first.Cursor });
        await Assert.That(second.Rows.Single().EntityId).IsEqualTo("d127");
        await Assert.That(second.Cursor).IsNull();
        var all = engine.Execute(Reader, new(db.Partition, "SELECT TOP 128 id FROM orders ORDER BY id", AllowFullScan: true));
        await Assert.That(all.Rows.Length).IsEqualTo(64);
        await Assert.That(all.Rows[0].EntityId).IsEqualTo("d001");
        db.Commit(new PutDocument(Collection, "d126", "{\"id\":\"d126\",\"amount\":0}", ExplicitReplacement: true));
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader, request with { Cursor = first.Cursor }));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(engine.Execute(Reader, request).Rows.Single().EntityId).IsEqualTo("d125");
        _ = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, db.Partition.TenantId, [], [])
        {
            PolicyEpoch = 2
        })).Get<PrincipalRecord>();
        var before = db.Store.GetReadDiagnostics();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader, request));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Store.GetReadDiagnostics().RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _ = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(Root, request, cancellationToken: cancellation.Token));
        await Assert.That(engine.Execute(Root, request).Rows.Single().EntityId).IsEqualTo("d125");
    }

    [Test]
    public async Task AcAisql008PrimaryRangePrefixUnicodeAndDeletedRowsSurviveTwoColdOpens()
    {
        using var db = Create();
        db.Commit([.. new[] { "z", "za", "zя", "z😀" }.Select(id => (Mutation)new PutDocument(Collection, id,
            JsonSerializer.Serialize(new { id, amount = 0 })))]);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var exact = new QueryRequest(db.Partition, "SELECT id FROM orders WHERE id BETWEEN 'z' AND 'z' ORDER BY id");
        await Assert.That(engine.Execute(Root, exact).Rows.Single().EntityId).IsEqualTo("z");
        var between = new QueryRequest(db.Partition, "SELECT id FROM orders WHERE id > 'z' AND id < 'z😀' ORDER BY id");
        await Assert.That(string.Join(',', engine.Execute(Root, between).Rows.Select(row => row.EntityId))).IsEqualTo("za,zя");
        db.Commit(new DeleteDocument(Collection, "za"));
        await Assert.That(engine.Execute(Root, between).Rows.Single().EntityId).IsEqualTo("zя");
        var identity = db.Store.Identity;
        var cut = db.Store.Position;
        db.JoinOwnedResources();
        for (var cold = 0; cold < 2; cold++)
        {
            using var store = ZoneTreeExistingStore.Open(new(db.Directory) { Incarnation = identity.Incarnation },
                identity.NodeId, db.StorageExecution);
            var reopened = new QueryEngine(db.CreateConfiguredDatabase(store), UnitExecutionOptions.QueryExecution());
            await Assert.That(reopened.Execute(Root, exact).Rows.Single().EntityId).IsEqualTo("z");
            await Assert.That(reopened.Execute(Root, between).Rows.Single().EntityId).IsEqualTo("zя");
            await Assert.That(store.Position).IsEqualTo(cut);
        }
    }

    private static TestDatabase Create()
    {
        var db = new TestDatabase();
        try
        {
            var resource = new ResourceDefinition(Collection, ResourceKind.Collection, db.Partition.TransactionDomainId)
            {
                RelationalSchema = new(Id, [new(Id, RelationalColumnType.Text), new(Amount, RelationalColumnType.WholeNumber)])
            };
            _ = db.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
            db.Commit([.. Enumerable.Range(0, Records).Select(number => (Mutation)new PutDocument(Collection,
                "d" + number.ToString("D3", CultureInfo.InvariantCulture),
                string.Create(CultureInfo.InvariantCulture, $"{{\"id\":\"d{number:D3}\",\"amount\":{number}}}"),
                Access: new(number % 2 == 0 ? Other : Reader)))]);
            return db;
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            ServerFailureObserver.Observe(db.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}

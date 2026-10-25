using System.Buffers.Text;
using KeyLoad.Query;
using KeyLoad.Query.ChangeFeeds;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.FunctionalTests.QueryExecution;

/// <summary>AC-IS-001: real query cursors and persisted access paths consume only attributed execution records.</summary>
internal sealed class QueryCursorTests
{
    private const string Root = "root";
    private const string Collection = "orders";
    private const string Index = "status";
    private const string StatusPath = "/status";
    private const string First = "a";
    private const string Second = "b";
    private const string FirstJson = "{\"status\":\"open\",\"n\":1.2300e+02,\"large\":9007199254740993}";
    private const string SecondJson = "{\"status\":\"open\",\"text\":\"Україна\"}";
    private const string PageSql = "SELECT TOP (1) * FROM orders WHERE status = 'open' ORDER BY id";
    private const string LiveSql = "SELECT TOP (100) * FROM orders WHERE status = 'open'";
    private const string FullScanSql = "SELECT * FROM orders ORDER BY id";
    private const string IndexPath = "index:status";
    private const string RuntimePrefix = "KLT2.";
    private const string WrongHash = "different-query";
    private const char SignatureSeparator = '.';
    private const int SignatureBytes = 32;
    private const int One = 1;
    private const int Two = 2;

    [Test]
    public async Task CursorContinuesRealIndexedRecordsAndRetainsEveryClaim()
    {
        using var fixture = Fixture();
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(fixture.Partition, PageSql);
        var first = engine.Execute(Root, request);
        var cursor = first.Cursor!;
        var claims = fixture.Database.Verify<QueryCursorClaims>(cursor);
        var encoded = OrleansSerialization.Serialize(claims);
        await Assert.That(OrleansSerialization.Deserialize<QueryCursorClaims>(encoded)).IsEqualTo(claims);
        await Assert.That(OrleansSerialization.Measure(claims)).IsEqualTo((long)encoded.Length);
        await Assert.That(cursor.StartsWith(RuntimePrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(first.AccessPath).IsEqualTo(IndexPath);
        await Assert.That(first.Rows.Single().EntityId).IsEqualTo(First);
        await Assert.That(claims.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(claims.NodeId).IsEqualTo(fixture.Store.Identity.NodeId);
        await Assert.That(claims.PrincipalId).IsEqualTo(Root);
        await Assert.That(claims.Offset).IsEqualTo(0);
        await Assert.That(claims.AfterId).IsEqualTo(First);
        var second = engine.Execute(Root, request with
        {
            Cursor = cursor
        });
        await Assert.That(second.Rows.Single().EntityId).IsEqualTo(Second);
        await Assert.That(second.Rows.Single().Json).IsEqualTo(fixture.Database.GetDocument(Root, new(fixture.Partition, Collection, Second))!.Json);
        await Assert.That(second.Cursor).IsNull();
        await Assert.That(second.CutPosition).IsEqualTo(first.CutPosition);
        var full = engine.Execute(Root, new(fixture.Partition, FullScanSql, AllowFullScan: true));
        await Assert.That(full.Rows.Length).IsEqualTo(Two);
        await Assert.That(full.Rows.Single(row => row.EntityId == First).Json)
            .IsEqualTo(fixture.Database.GetDocument(Root, new(fixture.Partition, Collection, First))!.Json);
    }

    [Test]
    public async Task TamperedAndWrongQueryPageTokensRetainCursorExpiredContract()
    {
        using var fixture = Fixture();
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(fixture.Partition, PageSql);
        var cursor = engine.Execute(Root, request).Cursor!;
        var claims = fixture.Database.Verify<QueryCursorClaims>(cursor);
        var cases = new[] { Tamper(cursor), fixture.Database.Sign(claims with { QueryHash = WrongHash }) };
        foreach (var invalid in cases)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                engine.Execute(Root, request with
                {
                    Cursor = invalid
                })).Code).IsEqualTo(ErrorCode.CursorExpired);
        }
        await Assert.That(engine.Execute(Root, request with
        {
            Cursor = cursor
        }).Rows.Single().EntityId).IsEqualTo(Second);
    }

    [Test]
    public async Task LiveCursorRetainsNestedChangeTokenAndTamperFailsClosed()
    {
        using var fixture = Fixture();
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        var query = new AstQueryRequest(fixture.Partition, new SqlParser(LiveSql, UnitExecutionOptions.QueryExecution()).Parse());
        var snapshot = engine.StartLiveQuery(Root, new(query));
        var claims = fixture.Database.Verify<LiveCursorClaims>(snapshot.Cursor);
        await Assert.That(OrleansSerialization.Deserialize<LiveCursorClaims>(OrleansSerialization.Serialize(claims))).IsEqualTo(claims);
        await Assert.That(snapshot.Cursor.StartsWith(RuntimePrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(claims.ChangeCursor.StartsWith(RuntimePrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(claims.QueryHash).IsEqualTo(QueryEngine.QueryHash(QueryValidation.Normalize(query, UnitExecutionOptions.QueryExecution().Value)));
        var cases = new[] { Tamper(snapshot.Cursor), fixture.Database.Sign(claims with { QueryHash = WrongHash }) };
        foreach (var invalid in cases)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                engine.ReadLiveQuery(Root, new(query, invalid))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        }
        var unchanged = engine.ReadLiveQuery(Root, new(query, snapshot.Cursor));
        await Assert.That(unchanged.Changes.Length).IsEqualTo(0);
        await Assert.That(unchanged.ThroughSequence).IsEqualTo(snapshot.ThroughSequence);
        await Assert.That(unchanged.Cursor.StartsWith(RuntimePrefix, StringComparison.Ordinal)).IsTrue();
    }

    private static TestDatabase Fixture()
    {
        var fixture = new TestDatabase();
        try
        {
            fixture.Configure(Collection, ResourceKind.Collection, indexes: [new(Index, [StatusPath])]);
            fixture.Commit(new PutDocument(Collection, First, FirstJson), new PutDocument(Collection, Second, SecondJson));
            return fixture;
        }
        catch (Exception)
        {
            fixture.Dispose();
            throw;
        }
    }

    private static string Tamper(string token)
        => token[..(token.LastIndexOf(SignatureSeparator) + One)] + Base64Url.EncodeToString(new byte[SignatureBytes]);
}

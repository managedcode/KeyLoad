using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class EqualityAccessPathSupport
{
    internal const string Root = "root";
    internal const string Reader = "reader";
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Collection = "orders";
    internal const string StatusIndex = "status";
    internal const string StatusPath = "/status";
    internal const string PrimaryId = "a";
    internal const string SecondaryId = "b";
    internal const string ParameterName = "match";
    internal const string Open = "open";
    internal const string PointPath = "point";
    internal const string IndexPath = "index:status";
    internal const string FullScanPath = "bounded-full-scan";
    internal const string IndexedIds = "a,b";
    internal const string Separator = ",";
    internal const string PrimaryJson = "{\"status\":\"open\",\"n\":2}";
    internal const string SecondaryJson = "{\"status\":\"open\",\"n\":null}";
    internal const string OtherId = "c";
    internal const string OtherJson = "{\"status\":\"closed\"}";
    internal const string NullId = "n";
    internal const string NullJson = "{\"status\":null}";
    internal const string MissingId = "m";
    internal const string MissingJson = "{}";

    private const string PointLiteral = "SELECT * FROM orders WHERE id = 'a' ORDER BY id";
    private const string ReversedPointLiteral = "SELECT * FROM orders WHERE 'a' = id ORDER BY id";
    private const string PointParameter = "SELECT * FROM orders WHERE id = @match ORDER BY id";
    private const string ReversedPointParameter = "SELECT * FROM orders WHERE @match = id ORDER BY id";
    private const string IndexLiteral = "SELECT * FROM orders WHERE status = 'open' ORDER BY id";
    private const string ReversedIndexLiteral = "SELECT * FROM orders WHERE 'open' = status ORDER BY id";
    private const string IndexParameter = "SELECT * FROM orders WHERE status = @match ORDER BY id";
    private const string ReversedIndexParameter = "SELECT * FROM orders WHERE @match = status ORDER BY id";

    internal static TestDatabase Create(DatabaseLimits? limits = null)
    {
        var db = new TestDatabase(limits);
        try
        {
            db.Configure(Collection, ResourceKind.Collection, indexes: [new(StatusIndex, [StatusPath])]);
            db.Commit(new PutDocument(Collection, PrimaryId, PrimaryJson),
                new PutDocument(Collection, SecondaryId, SecondaryJson),
                new PutDocument(Collection, OtherId, OtherJson),
                new PutDocument(Collection, NullId, NullJson),
                new PutDocument(Collection, MissingId, MissingJson));
            return db;
        }
        catch (Exception)
        {
            db.Dispose();
            throw;
        }
    }

    internal static QueryRequest Request(TestDatabase db, bool indexed, bool parameterized, bool reversed)
    {
        var sql = (indexed, parameterized, reversed) switch
        {
            (false, false, false) => PointLiteral,
            (false, false, true) => ReversedPointLiteral,
            (false, true, false) => PointParameter,
            (false, true, true) => ReversedPointParameter,
            (true, false, false) => IndexLiteral,
            (true, false, true) => ReversedIndexLiteral,
            (true, true, false) => IndexParameter,
            (true, true, true) => ReversedIndexParameter
        };
        var parameters = parameterized
            ? new Dictionary<string, JsonElement> { [ParameterName] = JsonSerializer.SerializeToElement(indexed ? Open : PrimaryId) }
            : null;
        return new(db.Partition, sql, parameters);
    }

    internal static async Task SameRows(QueryPage expected, QueryPage actual)
    {
        await Assert.That(JsonDefaults.Serialize(actual.Rows).SequenceEqual(JsonDefaults.Serialize(expected.Rows))).IsTrue();
        await Assert.That(actual.CutPosition).IsEqualTo(expected.CutPosition);
        await Assert.That(actual.Cursor).IsEqualTo(expected.Cursor);
        await Assert.That(actual.AccessPath).IsEqualTo(expected.AccessPath);
    }

    internal static KeyLoadException Failure(QueryEngine engine, QueryRequest request, string principal = Root)
        => Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(principal, request));
}

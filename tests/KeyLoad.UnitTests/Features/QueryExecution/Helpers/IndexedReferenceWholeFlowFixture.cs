using System.Text.Json;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class IndexedReferenceWholeFlowFixture
{
    internal static void Seed(TestDatabase db)
    {
        db.Configure("orders", ResourceKind.Collection, indexes: [new("status", ["/status"])]);
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"open\",\"number\":2}"),
            new PutDocument("orders", "b", "{\"status\":\"open\",\"number\":1}"),
            new PutDocument("orders", "c", "{\"status\":\"closed\",\"number\":0}"),
            new PutDocument("orders", "d", "{\"status\":\"open\",\"number\":2}"),
            new PutDocument("orders", "e", "{\"status\":null,\"number\":0}"),
            new PutDocument("orders", "f", "{\"number\":0}"),
            new PutDocument("orders", "g", "{\"status\":\"open\",\"number\":3}"));
    }

    internal static QueryRequest Request(TestDatabase db, bool indexed, bool descending, int limit)
        => new(db.Partition, "SELECT id, status, number FROM orders WHERE " +
            (indexed ? "status = 'open'" : "status = 'open' OR number = -1") +
            " ORDER BY number " + (descending ? "DESC" : "ASC") + ", id ASC LIMIT " +
            limit.ToString(System.Globalization.CultureInfo.InvariantCulture), AllowFullScan: true);

    internal static string[] Bytes(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        { throw new InvalidOperationException("Fixture state exceeds the bounded snapshot."); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static async Task<QueryRow[]> PagesAsync(TestDatabase db, QueryEngine engine, QueryRequest request,
        string path, TimeProvider clock)
    {
        var rows = new List<QueryRow>();
        string? cursor = null;
        for (var pageIndex = 0; pageIndex < 4; pageIndex++)
        {
            var page = engine.Execute("root", request with { Cursor = cursor }, clock);
            await Assert.That(page.AccessPath).IsEqualTo(path);
            await Assert.That(page.CutPosition).IsEqualTo(db.Store.Position);
            rows.AddRange(page.Rows);
            cursor = page.Cursor;
            if (cursor is null)
            { return rows.ToArray(); }
        }
        throw new InvalidOperationException("Four matching records did not terminate within four bounded pages.");
    }

    internal static async Task LiteralRowsAsync(IEnumerable<QueryRow> rows, bool descending)
    {
        var ids = descending ? new[] { "g", "a", "d", "b" } : ["b", "a", "d", "g"];
        await Assert.That(rows.Select(row => row.EntityId)).IsEquivalentTo(ids, CollectionOrdering.Matching);
        foreach (var row in rows)
        {
            var number = row.EntityId switch { "b" => 1, "g" => 3, _ => 2 };
            await Assert.That(row.Json).IsEqualTo("{\"id\":\"" + row.EntityId + "\",\"status\":\"open\",\"number\":" + number + "}");
            await Assert.That(row.Revision).IsEqualTo(1L);
            await Assert.That(row.Redacted).IsFalse();
            await Assert.That(row.RedactedFields ?? []).IsEmpty();
            await Assert.That(row.Sources).IsNull();
        }
    }

    internal static string[] Projection(IEnumerable<QueryRow> rows)
        => rows.Select(row => JsonSerializer.Serialize(row, JsonDefaults.Options)).ToArray();
}

internal sealed class IndexedReferenceCursorClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

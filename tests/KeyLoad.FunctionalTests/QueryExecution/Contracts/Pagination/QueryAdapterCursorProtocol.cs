using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryAdapterCursorProtocol
{
    private const string Collection = "orders";
    private const int PageLimit = 1;
    private const decimal Boundary = 2m;
    private const long Revision = 1;
    private const string FullScan = "bounded-full-scan";
    internal const string Sql = "SELECT TOP (1) d.id, d.number FROM orders d WHERE d.number >= 2.00 ORDER BY d.id";
    internal const string EquivalentSql = "SELECT TOP (1) [d].[id], [d].[number] FROM [orders] [d] "
        + "WHERE (([d].[number] >= (2.000))) ORDER BY [d].[id] ASC";
    internal const string CursorRefused = "The query cursor no longer has a valid authorized read cut.";

    internal static AstQueryRequest Json(PartitionRef partition, string? cursor = null)
        => QueryAdapterTestSupport.RoundTrip(new(partition,
            new SqlParser(Sql.Replace("2.00", "2", StringComparison.Ordinal),
                UnitExecutionOptions.QueryExecution()).Parse(), new(), true, cursor));

    internal static KeyLoadQuery<QueryAdapterOrder> Builder(PartitionRef partition)
        => KeyLoadQuery.From<QueryAdapterOrder>(partition, Collection, UnitClientOptions.Translation())
            .Where(row => row.Number >= Boundary).OrderBy(row => QueryFunctions.DocumentId(row))
            .Select(row => new { Id = QueryFunctions.DocumentId(row), row.Number }).Take(PageLimit);

    internal static async Task FullPageAsync(QueryPage actual, string id, long position, bool continues)
    {
        var json = id switch
        {
            "a" => "{\"id\":\"a\",\"number\":2}",
            "b" => "{\"id\":\"b\",\"number\":3}",
            "c" => "{\"id\":\"c\",\"number\":4}",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        var rows = ImmutableArray.Create(new QueryRow(id, Revision, json, false, []));
        await Assert.That(JsonDefaults.Serialize(actual.Rows)).IsEquivalentTo(JsonDefaults.Serialize(rows), CollectionOrdering.Matching);
        await Assert.That(actual.CutPosition).IsEqualTo(position);
        await Assert.That(actual.AccessPath).IsEqualTo(FullScan);
        if (continues)
        {
            await Assert.That(actual.Cursor).IsNotNull();
        }
        else
        {
            await Assert.That(actual.Cursor).IsNull();
        }
    }
}

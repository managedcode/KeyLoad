using System.Globalization;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryAdapterTests
{
    private const int NumericValueCount = 20;
    private const int StatusCount = 3;
    private const int DocumentCount = 200;
    private const int QueryCount = 50;
    private const int PageLimit = 30;
    private const int DocumentStride = 29;
    private const int QueryStride = 7;
    private const int DocumentSeed = 9173;
    private const int QuerySeed = 9173;

    [Test]
    public async Task SqlJsonAndCSharpAdaptersHaveTheSameSeededResultsAndAccessPath()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new(QueryAdapterTestTokens.Status, [QueryAdapterTestTokens.StatusPath])]);
        var statuses = new[] { QueryAdapterTestTokens.Open, QueryAdapterTestTokens.Hold, QueryAdapterTestTokens.Closed };
        var documentCoverage = new int[StatusCount * NumericValueCount];
        var documents = Enumerable.Range(0, DocumentCount).Select(index =>
        {
            var number = (index * DocumentStride + DocumentSeed) % NumericValueCount;
            var statusIndex = index % StatusCount;
            documentCoverage[statusIndex * NumericValueCount + number]++;
            return new PutDocument("orders", "doc-" + index.ToString("D3", CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new QueryAdapterOrder(number, statuses[statusIndex]), JsonDefaults.Options));
        }).Cast<Mutation>().ToArray();
        db.Commit(documents);
        await QueryWorkloadCoverageAssertions.AssertDocumentCoverage(documentCoverage, StatusCount, NumericValueCount);

        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var thresholds = new bool[NumericValueCount];
        var queriedStatuses = new bool[StatusCount];
        var reachedPageLimit = false;
        for (var trial = 0; trial < QueryCount; trial++)
        {
            var minimum = (decimal)((trial * QueryStride + QuerySeed) % NumericValueCount);
            var statusIndex = trial % StatusCount;
            thresholds[(int)minimum] = true;
            queriedStatuses[statusIndex] = true;
            var status = statuses[statusIndex];
            var minimumText = minimum.ToString(CultureInfo.InvariantCulture);
            var sql = $"SELECT d.id, d.number, d.status FROM orders d WHERE d.status = '{status}' AND d.number >= {minimumText} ORDER BY d.number DESC, d.id LIMIT {PageLimit}";
            var expected = engine.Execute("root", new(db.Partition, sql));
            var json = engine.ExecuteAst("root", QueryAdapterTestSupport.RoundTrip(new(db.Partition, new SqlParser(sql, new()).Parse())));
            var csharp = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).Where(row => row.Status == status && row.Number >= minimum)
                .OrderByDescending(row => row.Number).ThenBy(row => QueryFunctions.DocumentId(row))
                .Select(row => new { Id = QueryFunctions.DocumentId(row), row.Number, row.Status }).Take(PageLimit);
            var actual = engine.ExecuteAst("root", QueryAdapterTestSupport.RoundTrip(csharp.ToRequest()));
            await QueryAdapterTestSupport.SameRows(expected, json);
            await QueryAdapterTestSupport.SameRows(expected, actual);
            await Assert.That(actual.AccessPath).IsEqualTo("index:status");
            await Assert.That(json.AccessPath).IsEqualTo(expected.AccessPath);
            reachedPageLimit |= actual.Rows.Length == PageLimit;
        }

        await Assert.That(thresholds.All(seen => seen)).IsTrue();
        await Assert.That(queriedStatuses.All(seen => seen)).IsTrue();
        await Assert.That(reachedPageLimit).IsTrue();
    }
}

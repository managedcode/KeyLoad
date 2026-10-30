using System.Globalization;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

/// <summary>AC-QUERY-009: deleted ranges do not prevent complete id-ordered page traversal.</summary>
internal sealed class QueryKeysetDeletedRangesTests
{
    private const string Root = "root";
    private const string Collection = "pages";
    private const string PageSql = "SELECT TOP (10) * FROM pages ORDER BY id";
    private const string IdFormat = "D6";
    private const string PayloadPrefix = "{\"value\":\"";
    private const string PayloadSuffix = "\"}";
    private const int EmptyPrefix = 0;
    private const int VisiblePrefix = 5;
    private const int DeletedRecords = 100_000;
    private const int TailRecords = 20;
    private const int BatchRecords = 1_000;
    private const int PageRows = 10;
    private const int PayloadCharacters = 512;

    [Test]
    [Arguments(EmptyPrefix)]
    [Arguments(VisiblePrefix)]
    public async Task AcQuery009PagesCrossDeletedRangesWithoutLosingLiveRows(int leadingRecords)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var database = CreateDatabase(leadingRecords, token);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var expected = Enumerable.Range(EmptyPrefix, leadingRecords)
            .Concat(Enumerable.Range(leadingRecords + DeletedRecords, TailRecords)).Select(Id).ToArray();
        var request = new QueryRequest(database.Partition, PageSql, AllowFullScan: true);
        var actual = new List<string>();
        string? cursor = null;
        do
        {
            var page = engine.Execute(Root, request with { Cursor = cursor }, cancellationToken: token);
            await Assert.That(page.Rows.Length > EmptyPrefix && page.Rows.Length <= PageRows).IsTrue();
            actual.AddRange(page.Rows.Select(row => row.EntityId));
            await Assert.That(actual.Count <= expected.Length).IsTrue();
            cursor = page.Cursor;
        } while (cursor is not null);

        await Assert.That(actual.SequenceEqual(expected, StringComparer.Ordinal)).IsTrue();
        var healthy = engine.Execute(Root, request, cancellationToken: token);
        await Assert.That(healthy.Rows.Select(row => row.EntityId)
            .SequenceEqual(expected.Take(PageRows), StringComparer.Ordinal)).IsTrue();
    }

    private static TestDatabase CreateDatabase(int leadingRecords, CancellationToken token)
    {
        var database = new TestDatabase();
        try
        {
            database.Configure(Collection, ResourceKind.Collection);
            var payload = PayloadPrefix + new string('x', PayloadCharacters) + PayloadSuffix;
            foreach (var chunk in Enumerable.Range(EmptyPrefix, leadingRecords + DeletedRecords + TailRecords).Chunk(BatchRecords))
            {
                token.ThrowIfCancellationRequested();
                database.Commit([.. chunk.Select(ordinal => new PutDocument(Collection, Id(ordinal), payload))]);
            }
            foreach (var chunk in Enumerable.Range(leadingRecords, DeletedRecords).Chunk(BatchRecords))
            {
                token.ThrowIfCancellationRequested();
                database.Commit([.. chunk.Select(ordinal => new DeleteDocument(Collection, Id(ordinal)))]);
            }
            return database;
        }
        catch (Exception)
        {
            database.Dispose();
            throw;
        }
    }

    private static string Id(int ordinal) => ordinal.ToString(IdFormat, CultureInfo.InvariantCulture);
}

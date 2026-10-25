using System.Globalization;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

/// <summary>AC-QUERY-009: a query ordered by document id pages through any collection size with work bounded per page.</summary>
internal sealed class QueryKeysetPagingTests
{
    private const string Root = "root";
    private const string Collection = "pages";
    private const string PageSql = "SELECT TOP (10) * FROM pages ORDER BY id";
    private const string FilteredSql = "SELECT TOP (10) * FROM pages WHERE parity = 'even' ORDER BY id";
    private const string FullScanPath = "bounded-full-scan";
    private const string IdFormat = "D3";
    private const string Even = "even";
    private const string Odd = "odd";
    private const int Documents = 200;
    private const int PageRows = 10;
    private const int Parity = 2;

    [Test]
    public async Task AcQuery009IdOrderedPagesReadEveryDocumentOnce()
    {
        using var fixture = Fixture();
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());

        var all = ReadAll(engine, new(fixture.Partition, PageSql, AllowFullScan: true), out var pages);
        var even = ReadAll(engine, new(fixture.Partition, FilteredSql, AllowFullScan: true), out var evenPages);

        await Assert.That(all).IsEquivalentTo(Enumerable.Range(0, Documents).Select(Id));
        await Assert.That(pages).IsEqualTo(Documents / PageRows);
        await Assert.That(even).IsEquivalentTo(Enumerable.Range(0, Documents).Where(IsEven).Select(Id));
        await Assert.That(evenPages).IsEqualTo(Documents / Parity / PageRows);
    }

    [Test]
    public async Task AcQuery009FullScansServeEveryDocumentInIdOrderedPages()
    {
        using var fixture = Fixture();
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());

        var page = engine.Execute(Root, new(fixture.Partition, PageSql, AllowFullScan: true));
        var unordered = engine.Execute(Root, new(fixture.Partition, "SELECT * FROM pages", AllowFullScan: true));
        var all = ReadAll(engine, new(fixture.Partition, PageSql, AllowFullScan: true), out var pages);

        await Assert.That(page.AccessPath).IsEqualTo(FullScanPath);
        await Assert.That(page.Rows.Select(row => row.EntityId)).IsEquivalentTo(Enumerable.Range(0, PageRows).Select(Id));
        await Assert.That(page.Cursor).IsNotNull();
        await Assert.That(unordered.AccessPath).IsEqualTo(FullScanPath);
        await Assert.That(unordered.Rows.Length).IsGreaterThan(PageRows);
        await Assert.That(all).IsEquivalentTo(Enumerable.Range(0, Documents).Select(Id));
        await Assert.That(pages).IsEqualTo(Documents / PageRows);
    }

    private static List<string> ReadAll(QueryEngine engine, QueryRequest request, out int pages)
    {
        var ids = new List<string>();
        pages = 0;
        string? cursor = null;
        do
        {
            var page = engine.Execute(Root, request with { Cursor = cursor });
            pages++;
            ids.AddRange(page.Rows.Select(row => row.EntityId));
            cursor = page.Cursor;
        } while (cursor is not null);
        return ids;
    }

    private static TestDatabase Fixture()
    {
        var fixture = new TestDatabase();
        try
        {
            fixture.Configure(Collection, ResourceKind.Collection);
            foreach (var chunk in Enumerable.Range(0, Documents).Chunk(PageRows))
            {
                fixture.Commit([.. chunk.Select(ordinal => new PutDocument(Collection, Id(ordinal),
                    "{\"parity\":\"" + (IsEven(ordinal) ? Even : Odd) + "\"}"))]);
            }
            return fixture;
        }
        catch (Exception)
        {
            fixture.Dispose();
            throw;
        }
    }

    private static bool IsEven(int ordinal) => ordinal % Parity == 0;

    private static string Id(int ordinal) => ordinal.ToString(IdFormat, CultureInfo.InvariantCulture);
}

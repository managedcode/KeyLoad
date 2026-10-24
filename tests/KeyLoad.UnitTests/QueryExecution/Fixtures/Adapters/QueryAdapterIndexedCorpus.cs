using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryAdapterIndexedCorpus
{
    internal const int NumericValueCount = 20;
    internal const int StatusCount = 3;
    internal const int DocumentCount = 200;
    internal const int QueryCount = 50;
    internal const int PageLimit = 30;
    private const int DocumentStride = 29;
    private const int DocumentSeed = 9173;
    private const int QueryStride = 7;
    private const int QuerySeed = 9173;
    private const long Revision = 1;
    internal const string Collection = "orders";
    internal const string Principal = "root";
    internal const string IndexPath = "index:status";

    internal static string[] Statuses() => [QueryAdapterTestTokens.Open, QueryAdapterTestTokens.Hold, QueryAdapterTestTokens.Closed];
    internal static decimal Minimum(int trial) => (trial * QueryStride + QuerySeed) % NumericValueCount;
    internal static (Mutation[] Documents, int[] Coverage) Seed()
    {
        var statuses = Statuses();
        var coverage = new int[StatusCount * NumericValueCount];
        var documents = Enumerable.Range(0, DocumentCount).Select(index =>
        {
            var number = (index * DocumentStride + DocumentSeed) % NumericValueCount;
            var statusIndex = index % StatusCount;
            coverage[statusIndex * NumericValueCount + number]++;
            return new PutDocument(Collection, "doc-" + index.ToString("D3", CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new QueryAdapterOrder(number, statuses[statusIndex]), JsonDefaults.Options));
        }).Cast<Mutation>().ToArray();
        return (documents, coverage);
    }

    internal static (ImmutableArray<QueryRow> Rows, bool Continues) Expected(decimal minimum, string status)
    {
        var statuses = Statuses();
        var selected = Enumerable.Range(0, DocumentCount)
            .Select(index => new
            {
                Id = "doc-" + index.ToString("D3", CultureInfo.InvariantCulture),
                Number = (index * DocumentStride + DocumentSeed) % NumericValueCount,
                Status = statuses[index % StatusCount]
            })
            .Where(row => row.Status == status && row.Number >= minimum)
            .OrderByDescending(row => row.Number).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var rows = selected.Take(PageLimit).Select(row => new QueryRow(row.Id, Revision,
            "{\"id\":\"" + row.Id + "\",\"number\":" + row.Number.ToString(CultureInfo.InvariantCulture)
                + ",\"status\":\"" + row.Status + "\"}", false, [])).ToImmutableArray();
        return (rows, selected.Length > PageLimit);
    }
}

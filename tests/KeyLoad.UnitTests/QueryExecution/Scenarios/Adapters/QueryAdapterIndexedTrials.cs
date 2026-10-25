using System.Globalization;
using KeyLoad.Client;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryAdapterIndexedTrials
{
    internal static async Task RunAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition)
    {
        var statuses = QueryAdapterIndexedCorpus.Statuses();
        var thresholds = new bool[QueryAdapterIndexedCorpus.NumericValueCount];
        var queriedStatuses = new bool[QueryAdapterIndexedCorpus.StatusCount];
        var reachedPageLimit = false;
        for (var trial = 0; trial < QueryAdapterIndexedCorpus.QueryCount; trial++)
        {
            var minimum = QueryAdapterIndexedCorpus.Minimum(trial);
            var statusIndex = trial % QueryAdapterIndexedCorpus.StatusCount;
            thresholds[(int)minimum] = true;
            queriedStatuses[statusIndex] = true;
            var status = statuses[statusIndex];
            var minimumText = minimum.ToString(CultureInfo.InvariantCulture);
            var sql = $"SELECT TOP ({QueryAdapterIndexedCorpus.PageLimit}) d.id, d.number, d.status FROM orders d "
                + $"WHERE d.status = '{status}' AND d.number >= {minimumText} ORDER BY d.number DESC, d.id";
            var independent = QueryAdapterIndexedCorpus.Expected(minimum, status);
            var expected = await QueryAdapterIndexedPages.ReadAsync(store, () => engine.Execute(QueryAdapterIndexedCorpus.Principal,
                new(partition, sql)), independent.Rows, independent.Continues);
            var json = await QueryAdapterIndexedPages.ReadAsync(store, () => engine.ExecuteAst(QueryAdapterIndexedCorpus.Principal,
                QueryAdapterTestSupport.RoundTrip(new(partition, new SqlParser(sql, UnitExecutionOptions.QueryExecution()).Parse()))),
                independent.Rows, independent.Continues);
            var csharp = KeyLoadQuery.From<QueryAdapterOrder>(partition, QueryAdapterIndexedCorpus.Collection, UnitClientOptions.Translation())
                .Where(row => row.Status == status && row.Number >= minimum)
                .OrderByDescending(row => row.Number).ThenBy(row => QueryFunctions.DocumentId(row))
                .Select(row => new { Id = QueryFunctions.DocumentId(row), row.Number, row.Status }).Take(QueryAdapterIndexedCorpus.PageLimit);
            var actual = await QueryAdapterIndexedPages.ReadAsync(store, () => engine.ExecuteAst(QueryAdapterIndexedCorpus.Principal,
                QueryAdapterTestSupport.RoundTrip(csharp.ToRequest())), independent.Rows, independent.Continues);
            await QueryAdapterTestSupport.SameRows(expected, json);
            await QueryAdapterTestSupport.SameRows(expected, actual);
            await Assert.That(actual.AccessPath).IsEqualTo("index:status");
            await Assert.That(json.AccessPath).IsEqualTo(expected.AccessPath);
            reachedPageLimit |= actual.Rows.Length == QueryAdapterIndexedCorpus.PageLimit;
        }
        await Assert.That(thresholds.All(seen => seen)).IsTrue();
        await Assert.That(queriedStatuses.All(seen => seen)).IsTrue();
        await Assert.That(reachedPageLimit).IsTrue();
    }
}

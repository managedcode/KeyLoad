using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Search;

/// <summary>The full-text scenario, shared by every search engine and KeyLoad.</summary>
internal static class SearchScenario
{
    internal const string Load = "load";
    internal const string Index = "index";
    internal const string TermQuery = "term-query";
    internal const string AndQuery = "and-query";
    internal const int Limit = 10;
    private const string WrongResult = "Wrong full-text result for document ";

    internal static async Task<CaseResult> LoadAsync(ISearchDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        await driver.PrepareAsync(token);
        return await OperationLanes.RunBatchesAsync(Load, settings.Records, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.InsertAsync(
                [.. Enumerable.Range(first, count).Select(number => SearchDataset.Create(number, settings.Seed))], cancellation), token);
    }

    /// <summary>One call that makes the whole dataset searchable; its time is the index build time.</summary>
    internal static Task<CaseResult> IndexAsync(ISearchDriver driver, BenchmarkSettings _, CancellationToken token)
        => OperationLanes.RunAsync(Index, 1, 1, (_, cancellation) => driver.BuildIndexAsync(cancellation), token);

    /// <summary>One word of a stable document; results must contain the word.</summary>
    internal static Task<CaseResult> TermQueryAsync(ISearchDriver driver, BenchmarkSettings settings, CancellationToken token)
        => QueryAsync(TermQuery, driver, settings, 1, token);

    /// <summary>Two words of the same document; results must contain both, and that document guarantees a match.</summary>
    internal static Task<CaseResult> AndQueryAsync(ISearchDriver driver, BenchmarkSettings settings, CancellationToken token)
        => QueryAsync(AndQuery, driver, settings, 2, token);

    private static Task<CaseResult> QueryAsync(string id, ISearchDriver driver, BenchmarkSettings settings, int terms,
        CancellationToken token)
        => OperationLanes.RunAsync(id, settings.Operations, settings.Clients, async (index, cancellation) =>
        {
            var number = settings.Spread(index, 0, settings.Records);
            var words = SearchDataset.Words(number, settings.Seed).Distinct(StringComparer.Ordinal).Take(terms).ToArray();
            var found = await driver.SearchAsync(words, Limit, cancellation);
            if (found.Count == 0 || found.Count > Limit || !found.All(match => SearchDataset.Contains(match, words, settings.Seed)))
            {
                throw new InvalidDataException(WrongResult + SearchDataset.Id(number));
            }
        }, token);
}

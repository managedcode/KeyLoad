using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>The vector scenario, shared by every vector database and KeyLoad.</summary>
internal static class VectorScenario
{
    internal const string Load = "load";
    internal const string ExactTop10 = "exact-top10";
    private const string WrongNeighbors = "Wrong exact top-10 for query ";

    internal static async Task<CaseResult> LoadAsync(IVectorDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        await driver.PrepareAsync(VectorDataset.Dimensions, token);
        return await OperationLanes.RunBatchesAsync(Load, settings.Records, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.InsertAsync(
                [.. Enumerable.Range(first, count).Select(number => VectorDataset.Create(number, settings.Seed))], cancellation), token);
    }

    /// <summary>Cycles over the query vectors; every answer must equal the independently computed exact top-10.</summary>
    internal static Task<CaseResult> ExactTop10Async(IVectorDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var expected = VectorOracle.Compute(settings.Records, settings.Seed);
        var queries = Enumerable.Range(0, VectorDataset.Queries).Select(query => VectorDataset.Query(query, settings.Records, settings.Seed)).ToArray();
        return OperationLanes.RunAsync(ExactTop10, settings.Operations, settings.Clients, async (index, cancellation) =>
        {
            var query = index % VectorDataset.Queries;
            var found = await driver.NearestAsync(queries[query], VectorDataset.TopK, cancellation);
            if (found.Count != VectorDataset.TopK || !expected[query].SetEquals(found))
            {
                throw new InvalidDataException(WrongNeighbors + query);
            }
        }, token);
    }
}

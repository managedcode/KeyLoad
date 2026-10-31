using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Analytics;

/// <summary>The analytics scenario, shared by every analytics database and KeyLoad.</summary>
internal static class AnalyticsScenario
{
    internal const string Load = "load";
    internal const string GroupBy = "group-by";
    internal const string FilteredAggregate = "filtered-aggregate";
    private const string WrongTotals = "Wrong totals for the window starting at event ";

    internal static async Task<CaseResult> LoadAsync(IAnalyticsDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        if (settings.Records <= AnalyticsDataset.WindowEvents)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Records, null);
        }
        await driver.PrepareAsync(token);
        return await OperationLanes.RunBatchesAsync(Load, settings.Records, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.InsertAsync(
                [.. Enumerable.Range(first, count).Select(AnalyticsDataset.Create)], cancellation), token);
    }

    /// <summary>
    /// Sum of amount by region over a window of 10,000 events. Every such window repeats the 1,000-value amount
    /// cycle exactly ten times, so all windows share one expected answer, computed once from the dataset.
    /// </summary>
    internal static Task<CaseResult> GroupByAsync(IAnalyticsDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var expected = AnalyticsOracle.ByRegion(0);
        return OperationLanes.RunAsync(GroupBy, settings.Operations, settings.Clients, settings.CaseLimit, async (index, cancellation) =>
        {
            var first = Window(settings, index);
            var found = await driver.SumByRegionAsync(AnalyticsDataset.At(first),
                AnalyticsDataset.At(first + AnalyticsDataset.WindowEvents), cancellation);
            if (!AnalyticsOracle.Same(found, expected))
            {
                throw new InvalidDataException(WrongTotals + first);
            }
        }, token);
    }

    /// <summary>Sum of amount of one product over a window of 10,000 events; like the group-by, every window has the same answer.</summary>
    internal static Task<CaseResult> FilteredAggregateAsync(IAnalyticsDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var expected = Enumerable.Range(0, AnalyticsDataset.Products).Select(product => AnalyticsOracle.ForProduct(0, product)).ToArray();
        return OperationLanes.RunAsync(FilteredAggregate, settings.Operations, settings.Clients, settings.CaseLimit, async (index, cancellation) =>
        {
            var first = Window(settings, index);
            var product = index % AnalyticsDataset.Products;
            var found = await driver.SumForProductAsync(AnalyticsDataset.Product(product), AnalyticsDataset.At(first),
                AnalyticsDataset.At(first + AnalyticsDataset.WindowEvents), cancellation);
            if (!AnalyticsOracle.Same(found, expected[product]))
            {
                throw new InvalidDataException(WrongTotals + first);
            }
        }, token);
    }

    private static int Window(BenchmarkSettings settings, int index)
        => settings.Spread(index, 0, settings.Records - AnalyticsDataset.WindowEvents);
}

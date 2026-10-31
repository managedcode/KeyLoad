namespace KeyLoad.ComparisonHost.Analytics;

/// <summary>Expected totals for any window of <see cref="AnalyticsDataset.WindowEvents"/> events, computed directly from the dataset.</summary>
internal static class AnalyticsOracle
{
    internal static GroupTotal[] ByRegion(int first) => [.. Enumerable.Range(first, AnalyticsDataset.WindowEvents)
        .GroupBy(number => number % AnalyticsDataset.Regions)
        .Select(group => new GroupTotal(AnalyticsDataset.Region(group.Key), group.Count(), group.Sum(AnalyticsDataset.Amount)))];

    internal static GroupTotal ForProduct(int first, int product)
    {
        var numbers = Enumerable.Range(first, AnalyticsDataset.WindowEvents)
            .Where(number => number % AnalyticsDataset.Products == product).ToArray();
        return new(AnalyticsDataset.Product(product), numbers.Length, numbers.Sum(AnalyticsDataset.Amount));
    }

    internal static bool Same(IReadOnlyList<GroupTotal> actual, IReadOnlyList<GroupTotal> expected)
        => actual.Count == expected.Count && expected.All(group => actual.Any(found => Same(found, group)));

    internal static bool Same(GroupTotal actual, GroupTotal expected)
        => string.Equals(actual.Key, expected.Key, StringComparison.Ordinal) && actual.Count == expected.Count
            && AnalyticsDataset.Close(actual.Sum, expected.Sum);
}

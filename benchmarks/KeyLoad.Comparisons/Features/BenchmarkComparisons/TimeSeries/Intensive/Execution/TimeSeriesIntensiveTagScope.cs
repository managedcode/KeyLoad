namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveTagScope
{
    private string? successfulRaw;
    private string? successfulCanonical;

    internal string Canonical(string tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (string.Equals(tags, TimeSeriesIntensiveProfile.Tags, StringComparison.Ordinal))
        {
            return TimeSeriesIntensiveProfile.Tags;
        }

        if (string.Equals(tags, successfulRaw, StringComparison.Ordinal))
        {
            return successfulCanonical!;
        }

        var canonical = TimeSeriesJsonCanonicalizer.Canonicalize(tags);
        if (string.Equals(canonical, TimeSeriesIntensiveProfile.Tags, StringComparison.Ordinal))
        {
            successfulRaw = tags;
            successfulCanonical = TimeSeriesIntensiveProfile.Tags;
            return TimeSeriesIntensiveProfile.Tags;
        }

        return canonical;
    }
}

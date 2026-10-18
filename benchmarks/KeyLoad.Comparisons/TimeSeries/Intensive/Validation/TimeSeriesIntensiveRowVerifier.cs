using System.Text.Json;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRowVerifier
{
    internal static string Validate(SampleRecord? expected, SampleRecord? actual, TimeSeriesIntensiveTagScope tags)
    {
        if (expected is null || actual is null || actual.Sample is null || expected.Sample is null
            || actual.Sequence != expected.Sequence || !string.Equals(actual.SeriesId, expected.SeriesId, StringComparison.Ordinal)
            || !string.Equals(actual.Sample.EventId, expected.Sample.EventId, StringComparison.Ordinal)
            || actual.Sample.Timestamp != expected.Sample.Timestamp || !double.IsFinite(actual.Sample.Value)
            || actual.Sample.Value != expected.Sample.Value || actual.TagsJson is null || expected.TagsJson is null)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.Sample);
        }

        try
        {
            var canonical = tags.Canonical(actual.TagsJson);
            if (string.Equals(tags.Canonical(expected.TagsJson), canonical, StringComparison.Ordinal))
            {
                return canonical;
            }
        }
        catch (JsonException)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.Sample);
        }

        throw new ComparisonFailureException(TimeSeriesIntensiveErrors.Sample);
    }
}

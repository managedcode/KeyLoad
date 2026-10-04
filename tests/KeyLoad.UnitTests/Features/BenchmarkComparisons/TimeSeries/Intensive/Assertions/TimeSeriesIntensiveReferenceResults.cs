using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveReferenceResults
{
    internal static string Raw(ImmutableArray<SampleRecord> samples)
    {
        using var frame = new TimeSeriesIntensiveReferenceFramer("keyload.timeseries-intensive.result.raw");
        frame.Text("samples");
        frame.Count(samples.Length);
        foreach (var sample in samples)
        {
            Row(frame, sample);
        }

        return frame.Finish();
    }

    internal static string Latest(SampleRecord? sample)
    {
        using var frame = new TimeSeriesIntensiveReferenceFramer("keyload.timeseries-intensive.result.latest");
        frame.Text("sample");
        frame.Present(sample is not null);
        if (sample is not null)
        {
            Row(frame, sample);
        }

        return frame.Finish();
    }

    internal static string Aggregate(SampleAggregate aggregate)
    {
        using var frame = new TimeSeriesIntensiveReferenceFramer("keyload.timeseries-intensive.result.aggregate");
        Statistics(frame, aggregate);
        return frame.Finish();
    }

    internal static string Windows(ImmutableArray<SampleAggregateWindow> windows)
    {
        using var frame = new TimeSeriesIntensiveReferenceFramer("keyload.timeseries-intensive.result.windows");
        frame.Text("windows");
        frame.Count(windows.Length);
        foreach (var window in windows)
        {
            frame.Field("fromUtcTicks", window.From.UtcTicks);
            frame.Optional("untilExclusiveUtcTicks", window.UntilExclusive?.UtcTicks);
            frame.Text("aggregate");
            Statistics(frame, window.Aggregate);
        }

        return frame.Finish();
    }

    internal static string Receipt(Guid commandId, long sequence)
    {
        using var frame = new TimeSeriesIntensiveReferenceFramer("keyload.timeseries-intensive.result.append-receipt");
        frame.Field("commandId", commandId.ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        frame.Field("sequence", sequence);
        return frame.Finish();
    }

    internal static void Row(TimeSeriesIntensiveReferenceFramer frame, SampleRecord sample)
    {
        frame.Field("seriesId", sample.SeriesId);
        frame.Field("eventId", sample.Sample.EventId);
        frame.Field("timestampUtcTicks", sample.Sample.Timestamp.UtcTicks);
        frame.Field("sequence", sample.Sequence);
        frame.Field("valueBits", BitConverter.DoubleToInt64Bits(sample.Sample.Value));
        frame.Field("tags", sample.TagsJson);
    }

    private static void Statistics(TimeSeriesIntensiveReferenceFramer frame, SampleAggregate value)
    {
        frame.Field("count", value.Count);
        frame.Field("sumBits", BitConverter.DoubleToInt64Bits(value.Sum));
        frame.Optional("minimumBits", value.Minimum.HasValue ? BitConverter.DoubleToInt64Bits(value.Minimum.Value) : null);
        frame.Optional("maximumBits", value.Maximum.HasValue ? BitConverter.DoubleToInt64Bits(value.Maximum.Value) : null);
        frame.Optional("averageBits", value.Average.HasValue ? BitConverter.DoubleToInt64Bits(value.Average.Value) : null);
    }
}

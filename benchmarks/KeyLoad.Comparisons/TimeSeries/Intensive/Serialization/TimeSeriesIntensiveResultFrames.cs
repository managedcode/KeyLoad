namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveResultFrames
{
    internal static void Sample(TimeSeriesIntensiveDigestWriter frame, SampleRecord sample, string canonicalTags)
    {
        frame.Field(TimeSeriesIntensiveFrameLabels.SeriesId, sample.SeriesId);
        frame.Field(TimeSeriesIntensiveFrameLabels.EventId, sample.Sample.EventId);
        frame.Field(TimeSeriesIntensiveFrameLabels.TimestampUtcTicks, sample.Sample.Timestamp.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.Sequence, sample.Sequence);
        frame.Field(TimeSeriesIntensiveFrameLabels.ValueBits, BitConverter.DoubleToInt64Bits(sample.Sample.Value));
        frame.Field(TimeSeriesIntensiveFrameLabels.Tags, canonicalTags);
    }

    internal static void AppendSample(TimeSeriesIntensiveDigestWriter frame, SampleData sample)
    {
        frame.Field(TimeSeriesIntensiveFrameLabels.EventId, sample.EventId);
        frame.Field(TimeSeriesIntensiveFrameLabels.TimestampUtcTicks, sample.Timestamp.UtcTicks);
        frame.Field(TimeSeriesIntensiveFrameLabels.ValueBits, BitConverter.DoubleToInt64Bits(sample.Value));
        frame.Field(TimeSeriesIntensiveFrameLabels.Tags, TimeSeriesIntensiveProfile.Tags);
    }

    internal static void Aggregate(TimeSeriesIntensiveDigestWriter frame, SampleAggregate aggregate)
    {
        frame.Field(TimeSeriesIntensiveFrameLabels.Count, aggregate.Count);
        frame.Field(TimeSeriesIntensiveFrameLabels.SumBits, BitConverter.DoubleToInt64Bits(aggregate.Sum));
        frame.Optional(TimeSeriesIntensiveFrameLabels.MinimumBits, (Bits(aggregate.Minimum)).Value);
        frame.Optional(TimeSeriesIntensiveFrameLabels.MaximumBits, (Bits(aggregate.Maximum)).Value);
        frame.Optional(TimeSeriesIntensiveFrameLabels.AverageBits, (Bits(aggregate.Average)).Value);
    }

    private static (bool HasValue, long? Value) Bits(double? value) => value.HasValue ? (true, ((long?)(BitConverter.DoubleToInt64Bits(value.Value)))) : (false, default(long?));

}

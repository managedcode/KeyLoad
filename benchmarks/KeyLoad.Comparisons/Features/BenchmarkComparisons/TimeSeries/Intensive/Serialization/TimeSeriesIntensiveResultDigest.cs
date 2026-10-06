using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveResultDigest
{
    internal static string Raw(ImmutableArray<SampleRecord> samples)
    {
        if (samples.IsDefault)
        {
            throw new ArgumentException(TimeSeriesIntensiveErrors.UninitializedSamples, nameof(samples));
        }

        return RawCore(samples, default, false);
    }

    internal static string ValidatedRaw(ImmutableArray<SampleRecord> expected, ImmutableArray<SampleRecord> actual)
    {
        if (expected.IsDefault || actual.IsDefault || expected.Length != actual.Length)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.SampleCardinality);
        }

        return RawCore(actual, expected, true);
    }

    internal static string Latest(SampleRecord? sample) => LatestCore(null, sample, false);

    internal static string ValidatedLatest(SampleRecord? expected, SampleRecord? actual) => LatestCore(expected, actual, true);

    internal static string Aggregate(SampleAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        using var frame = new TimeSeriesIntensiveDigestWriter(TimeSeriesIntensiveFrameLabels.ResultDomain + TimeSeriesIntensiveFrameLabels.Aggregate);
        TimeSeriesIntensiveResultFrames.Aggregate(frame, aggregate);
        return frame.Finish();
    }

    internal static string Windows(ImmutableArray<SampleAggregateWindow> windows)
    {
        if (windows.IsDefault)
        {
            throw new ArgumentException(TimeSeriesIntensiveErrors.UninitializedWindows, nameof(windows));
        }

        using var frame = new TimeSeriesIntensiveDigestWriter(TimeSeriesIntensiveFrameLabels.ResultDomain + TimeSeriesIntensiveFrameLabels.Windows);
        frame.String(TimeSeriesIntensiveFrameLabels.Windows);
        frame.Count(windows.Length);
        foreach (var window in windows)
        {
            frame.Field(TimeSeriesIntensiveFrameLabels.FromUtcTicks, window.From.UtcTicks);
            frame.Optional(TimeSeriesIntensiveFrameLabels.UntilExclusiveUtcTicks, window.UntilExclusive?.UtcTicks);
            frame.String(TimeSeriesIntensiveFrameLabels.Aggregate);
            TimeSeriesIntensiveResultFrames.Aggregate(frame, window.Aggregate);
        }

        return frame.Finish();
    }

    internal static string AppendReceipt(TimeSeriesIntensiveAppendReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        using var frame = new TimeSeriesIntensiveDigestWriter(TimeSeriesIntensiveFrameLabels.ResultDomain + TimeSeriesIntensiveFrameLabels.AppendReceipt);
        frame.Field(TimeSeriesIntensiveFrameLabels.CommandId, receipt.CommandId.ToString(TimeSeriesIntensiveProfile.GuidFormat, CultureInfo.InvariantCulture));
        frame.Field(TimeSeriesIntensiveFrameLabels.Sequence, receipt.Sequence);
        return frame.Finish();
    }

    private static string RawCore(ImmutableArray<SampleRecord> samples, ImmutableArray<SampleRecord> expected, bool validate)
    {
        const int FirstElementIndex = 0;

        var tags = new TimeSeriesIntensiveTagScope();
        using var frame = new TimeSeriesIntensiveDigestWriter(TimeSeriesIntensiveFrameLabels.ResultDomain + TimeSeriesIntensiveFrameLabels.Raw);
        frame.String(TimeSeriesIntensiveFrameLabels.Samples);
        frame.Count(samples.Length);
        for (var index = FirstElementIndex; index < samples.Length; index++)
        {
            var sample = samples[index];
            var canonical = validate ? TimeSeriesIntensiveRowVerifier.Validate(expected[index], sample, tags)
                : tags.Canonical(sample.TagsJson);
            TimeSeriesIntensiveResultFrames.Sample(frame, sample, canonical);
        }

        return frame.Finish();
    }

    private static string LatestCore(SampleRecord? expected, SampleRecord? actual, bool validate)
    {
        var tags = new TimeSeriesIntensiveTagScope();
        string? canonical = null;
        if (validate && (expected is not null || actual is not null))
        {
            canonical = TimeSeriesIntensiveRowVerifier.Validate(expected, actual, tags);
        }
        else if (actual is not null)
        {
            canonical = tags.Canonical(actual.TagsJson);
        }

        using var frame = new TimeSeriesIntensiveDigestWriter(TimeSeriesIntensiveFrameLabels.ResultDomain + TimeSeriesIntensiveFrameLabels.Latest);
        frame.String(TimeSeriesIntensiveFrameLabels.Sample);
        frame.Present(actual is not null);
        if (actual is not null)
        {
            TimeSeriesIntensiveResultFrames.Sample(frame, actual, canonical!);
        }

        return frame.Finish();
    }
}

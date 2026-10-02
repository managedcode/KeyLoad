using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonWorkloadFactory
{
    private const string TenantPrefix = "benchmark-ts-";
    private const string DatabaseName = "comparison";
    private const string TransactionDomain = "shared";
    private const string PartitionPrefix = "timeseries-";
    private const string SetNameValue = "samples";
    private const string SeriesIdValue = "metrics";
    private const string SampleIdPrefix = "sample-";
    private const int GroupCount = 12;
    private const int SamplesPerGroup = 4;
    private const int MinutesPerBucket = 5;
    private const int EmptyRangeStartMinute = 1;
    private const int EmptyRangeEndMinute = 4;
    private const int OffsetHours = 3;
    private const double ValueGroupScale = 10;
    private const double ValueOffsetScale = 0.25;
    private const string InvalidRangeErrorCode = "BudgetExceeded";
    private const string TagsJson = "{\"source\":\"benchmark\",\"unit\":\"value\"}";

    internal static TimeSeriesComparisonWorkload Create(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var partitionKey = PartitionPrefix + runId;
        var partition = new PartitionRef(TenantPrefix + runId, DatabaseName, TransactionDomain, partitionKey);
        var samples = CreateSamples();
        var first = samples.MinBy(sample => sample.Timestamp) ?? throw new InvalidOperationException();
        var last = samples.MaxBy(sample => sample.Timestamp) ?? throw new InvalidOperationException();
        var width = TimeSpan.FromMinutes(MinutesPerBucket);
        var ranges = CreateRanges(first.Timestamp, last.Timestamp, width, samples);
        var expectedBuckets = CreateExpectedBuckets(samples);
        return new(runId, partition, SetNameValue, SeriesIdValue, width, samples, expectedBuckets, ranges,
            ComputeHash(samples, width));
    }

    private static ImmutableArray<TimeSeriesSamplePoint> CreateSamples()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = ImmutableArray.CreateBuilder<TimeSeriesSamplePoint>(GroupCount * SamplesPerGroup);
        var sequence = 0L;
        for (var group = GroupCount - 1; group >= 0; group--)
        {
            for (var item = SamplesPerGroup - 1; item >= 0; item--)
            {
                var timestamp = start.AddMinutes(group * MinutesPerBucket);
                var value = group * ValueGroupScale + (item + 1) * ValueOffsetScale;
                samples.Add(new(SampleIdPrefix + group.ToString("D2", CultureInfo.InvariantCulture) + "-" +
                    item.ToString("D2", CultureInfo.InvariantCulture), timestamp, value, sequence++, TagsJson));
            }
        }

        return samples.MoveToImmutable();
    }

    private static ImmutableArray<TimeSeriesReadRange> CreateRanges(DateTimeOffset first, DateTimeOffset last,
        TimeSpan bucketWidth, ImmutableArray<TimeSeriesSamplePoint> samples)
    {
        var exact = samples[0].Timestamp;
        return
        [
            new("inclusive-full-range", first, last, samples.Length),
            new("inclusive-boundary", exact, exact, SamplesPerGroup),
            new("offset-normalized", exact.ToOffset(TimeSpan.FromHours(OffsetHours)),
                exact.ToOffset(TimeSpan.FromHours(OffsetHours)), SamplesPerGroup),
            new("empty-range", first.AddMinutes(EmptyRangeStartMinute), first.AddMinutes(EmptyRangeEndMinute), 0),
            new("invalid-range", last.Add(bucketWidth), first, 0, InvalidRangeErrorCode)
        ];
    }

    private static ImmutableArray<TimeSeriesBucketValue> CreateExpectedBuckets(
        ImmutableArray<TimeSeriesSamplePoint> samples)
        => [.. samples.GroupBy(sample => sample.Timestamp.ToUniversalTime())
            .OrderBy(group => group.Key)
            .Select(group => new TimeSeriesBucketValue(group.Key, group.Sum(sample => sample.Value)))];

    private static string ComputeHash(ImmutableArray<TimeSeriesSamplePoint> samples, TimeSpan bucketWidth)
    {
        var content = new StringBuilder();
        content.Append(bucketWidth.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var sample in samples)
        {
            content.Append(sample.EventId).Append('|')
                .Append(sample.Timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(sample.Value.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(sample.TagsJson).Append('|')
                .Append(sample.Sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }
}

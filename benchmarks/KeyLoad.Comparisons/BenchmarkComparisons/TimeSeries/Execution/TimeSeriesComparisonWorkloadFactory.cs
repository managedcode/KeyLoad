using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonWorkloadFactory
{
    private const string SampleIndexFormat = "D2";
    private const string SampleValueFormat = "R";

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
    [ImmutableTemporalData]
    private static readonly TimeSpan CorpusBucketWidth = TimeSpan.FromMinutes(MinutesPerBucket);
    [ImmutableTemporalData]
    private static readonly TimeSpan CorpusTimestampOffset = TimeSpan.FromHours(OffsetHours);
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
        var width = CorpusBucketWidth;
        var ranges = CreateRanges(first.Timestamp, last.Timestamp, width, samples);
        var expectedBuckets = CreateExpectedBuckets(samples);
        return new(runId, partition, SetNameValue, SeriesIdValue, width, samples, expectedBuckets, ranges,
            ComputeHash(samples, width));
    }

    private static ImmutableArray<TimeSeriesSamplePoint> CreateSamples()
    {
        const int CorpusStartYear = 2026;
        const int CorpusStartMonthAndDay = 1;
        const int NoObservedItems = 0;
        const long NoObservedItemsLong = 0L;
        const int AdjacentElementOffset = 1;
        const string IdentitySeparator = "-";

        var start = new DateTimeOffset(CorpusStartYear, CorpusStartMonthAndDay, CorpusStartMonthAndDay, NoObservedItems, NoObservedItems, NoObservedItems, TimeSpan.Zero);
        var samples = ImmutableArray.CreateBuilder<TimeSeriesSamplePoint>(GroupCount * SamplesPerGroup);
        var sequence = NoObservedItemsLong;
        for (var group = GroupCount - AdjacentElementOffset; group >= NoObservedItems; group--)
        {
            for (var item = SamplesPerGroup - AdjacentElementOffset; item >= NoObservedItems; item--)
            {
                var timestamp = start.AddMinutes(group * MinutesPerBucket);
                var value = group * ValueGroupScale + (item + AdjacentElementOffset) * ValueOffsetScale;
                samples.Add(new(SampleIdPrefix + group.ToString(SampleIndexFormat, CultureInfo.InvariantCulture) + IdentitySeparator +
                    item.ToString(SampleIndexFormat, CultureInfo.InvariantCulture), timestamp, value, sequence++, TagsJson));
            }
        }

        return samples.MoveToImmutable();
    }

    private static ImmutableArray<TimeSeriesReadRange> CreateRanges(DateTimeOffset first, DateTimeOffset last,
        TimeSpan bucketWidth, ImmutableArray<TimeSeriesSamplePoint> samples)
    {
        const string OffsetNormalizedToken = "offset-normalized";
        const string EmptyRangeToken = "empty-range";
        const string CreateRangesNameText = "invalid-range";

        const int FirstElementIndex = 0;
        const string InclusiveFullRangeToken = "inclusive-full-range";
        const string InclusiveBoundaryToken = "inclusive-boundary";
        const int NoObservedItems = 0;

        var exact = samples[FirstElementIndex].Timestamp;
        return
        [
            new(InclusiveFullRangeToken, first, last, samples.Length),
            new(InclusiveBoundaryToken, exact, exact, SamplesPerGroup),
            new(OffsetNormalizedToken, exact.ToOffset(CorpusTimestampOffset),
                exact.ToOffset(CorpusTimestampOffset), SamplesPerGroup),
            new(EmptyRangeToken, first.AddMinutes(EmptyRangeStartMinute), first.AddMinutes(EmptyRangeEndMinute), NoObservedItems),
            new(CreateRangesNameText, last.Add(bucketWidth), first, NoObservedItems, InvalidRangeErrorCode)
        ];
    }

    private static ImmutableArray<TimeSeriesBucketValue> CreateExpectedBuckets(
        ImmutableArray<TimeSeriesSamplePoint> samples)
        => [.. samples.GroupBy(sample => sample.Timestamp.ToUniversalTime())
            .OrderBy(group => group.Key)
            .Select(group => new TimeSeriesBucketValue(group.Key, group.Sum(sample => sample.Value)))];

    private static string ComputeHash(ImmutableArray<TimeSeriesSamplePoint> samples, TimeSpan bucketWidth)
    {
        const char LineFeed = '\n';
        const char ComponentSeparator = '|';

        var content = new StringBuilder();
        content.Append(bucketWidth.Ticks.ToString(CultureInfo.InvariantCulture)).Append(LineFeed);
        foreach (var sample in samples)
        {
            content.Append(sample.EventId).Append(ComponentSeparator)
                .Append(sample.Timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append(ComponentSeparator)
                .Append(sample.Value.ToString(SampleValueFormat, CultureInfo.InvariantCulture)).Append(ComponentSeparator)
                .Append(sample.TagsJson).Append(ComponentSeparator)
                .Append(sample.Sequence.ToString(CultureInfo.InvariantCulture)).Append(LineFeed);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }
}

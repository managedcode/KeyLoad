using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveCorpus
{
    internal static ImmutableArray<SampleRecord> SeedInsertion { get; } = CreateInsertion();
    internal static ImmutableArray<SampleRecord> SeedOrdered { get; } = CreateOrdered();

    internal static ImmutableArray<SampleData> SeedBatch(int batch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batch);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(batch, TimeSeriesIntensiveProfile.SeedBatchCount);
        var builder = ImmutableArray.CreateBuilder<SampleData>(TimeSeriesIntensiveProfile.SeedBatchSize);
        var first = batch * TimeSeriesIntensiveProfile.SeedBatchSize;
        for (var index = first; index < first + TimeSeriesIntensiveProfile.SeedBatchSize; index++)
        {
            builder.Add(SeedInsertion[index].Sample);
        }

        return builder.MoveToImmutable();
    }

    internal static SampleData AppendSample(int index)
    {
        ValidateIndex(index);
        var value = ((index + TimeSeriesIntensiveProfile.RandomSeed) % TimeSeriesIntensiveProfile.ValuePeriod - TimeSeriesIntensiveProfile.ValueCenter) + (index % TimeSeriesIntensiveProfile.ItemsPerGroup - TimeSeriesIntensiveProfile.ItemValueCenter) / (double)TimeSeriesIntensiveProfile.QuarterScale;
        return new(TimeSeriesIntensiveProfile.AppendEventPrefix + index.ToString(TimeSeriesIntensiveProfile.AppendEventFormat, CultureInfo.InvariantCulture),
            TimeSeriesIntensiveProfile.Epoch.AddDays(TimeSeriesIntensiveProfile.AppendEpochDays).AddMilliseconds(index), value);
    }

    internal static void ValidateIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TimeSeriesIntensiveProfile.OperationCount);
    }

    private static ImmutableArray<SampleRecord> CreateInsertion()
    {
        const int AdjacentElementOffset = 1;
        const int NoObservedItems = 0;

        var builder = ImmutableArray.CreateBuilder<SampleRecord>(TimeSeriesIntensiveProfile.SampleCount);
        for (var original = TimeSeriesIntensiveProfile.SampleCount - AdjacentElementOffset; original >= NoObservedItems; original--)
        {
            builder.Add(Seed(original));
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<SampleRecord> CreateOrdered()
    {
        const int NoObservedItems = 0;
        const int AdjacentElementOffset = 1;
        const int SingleItemCount = 1;

        var builder = ImmutableArray.CreateBuilder<SampleRecord>(TimeSeriesIntensiveProfile.SampleCount);
        for (var timestamp = NoObservedItems; timestamp < TimeSeriesIntensiveProfile.TimestampCount; timestamp++)
        {
            var first = timestamp * TimeSeriesIntensiveProfile.TiedItems;
            for (var original = first + TimeSeriesIntensiveProfile.TiedItems - AdjacentElementOffset; original >= first; original--)
            {
                builder.Add(SeedInsertion[TimeSeriesIntensiveProfile.SampleCount - SingleItemCount - original]);
            }
        }

        return builder.MoveToImmutable();
    }

    private static SampleRecord Seed(int original)
    {
        var group = original / TimeSeriesIntensiveProfile.ItemsPerGroup;
        var item = original % TimeSeriesIntensiveProfile.ItemsPerGroup;
        var value = ((group + TimeSeriesIntensiveProfile.RandomSeed) % TimeSeriesIntensiveProfile.ValuePeriod - TimeSeriesIntensiveProfile.ValueCenter) + (item - TimeSeriesIntensiveProfile.ItemValueCenter) / (double)TimeSeriesIntensiveProfile.QuarterScale;
        return new(TimeSeriesIntensiveProfile.SeedSeries,
            new(TimeSeriesIntensiveProfile.SeedEventPrefix + original.ToString(TimeSeriesIntensiveProfile.SeedEventFormat, CultureInfo.InvariantCulture),
                TimeSeriesIntensiveProfile.Epoch.AddMinutes(group * TimeSeriesIntensiveProfile.GroupMinutes + item / TimeSeriesIntensiveProfile.TiedItems), value),
            TimeSeriesIntensiveProfile.SampleCount - original, TimeSeriesIntensiveProfile.Tags);
    }
}

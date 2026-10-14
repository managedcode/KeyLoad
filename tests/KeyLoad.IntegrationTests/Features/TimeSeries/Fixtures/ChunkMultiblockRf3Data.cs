using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Data
{
    internal const int OriginalCount = 257;
    internal const int HealthyCount = 258;
    private const int AddedCount = 254;
    private const int OffsetHours = -5;
    private const double EqualValue = 2;
    private const double SecondValue = 4;
    private const long FirstSequence = 1;
    private const long LateTick = 1;
    private const long FreshTick = 2;
    private const double FreshValue = 8;
    internal static SampleData Fresh => new("public-multiblock-fresh", TimeSeriesRf3Scenario.Start.AddTicks(FreshTick), FreshValue);

    internal static ImmutableArray<SampleData> Original =>
        [new("public-multiblock-first", TimeSeriesRf3Scenario.Start, EqualValue),
         new("public-multiblock-offset", TimeSeriesRf3Scenario.Start.ToOffset(TimeSpan.FromHours(OffsetHours)), SecondValue),
         new("public-multiblock-late", TimeSeriesRf3Scenario.Start.AddTicks(LateTick), -0d),
         .. Enumerable.Range(0, AddedCount).Select(index => new SampleData("public-multiblock-" + index.ToString("D3", CultureInfo.InvariantCulture),
             index % 2 == 0 ? TimeSeriesRf3Scenario.Start : TimeSeriesRf3Scenario.Start.ToOffset(TimeSpan.FromHours(OffsetHours)), EqualValue))];

    internal static SampleRecord[] Expected(bool healthy)
    {
        var original = Original.Select((sample, index) => new SampleRecord(TimeSeriesRf3Scenario.Series,
            sample, index + FirstSequence, TimeSeriesRf3Scenario.PublicTags));
        if (healthy)
        { original = original.Append(new(TimeSeriesRf3Scenario.Series, Fresh, HealthyCount, TimeSeriesRf3Scenario.PublicTags)); }
        return original.OrderBy(row => row.Sample.Timestamp.UtcTicks).ThenBy(row => row.Sequence).ToArray();
    }
}

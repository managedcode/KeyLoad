using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class ChunkMultiblockClusterData
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
    internal static SampleData Fresh => new("public-multiblock-fresh", TimeSeriesClusterScenario.Start.AddTicks(FreshTick), FreshValue);

    internal static ImmutableArray<SampleData> Original =>
        [new("public-multiblock-first", TimeSeriesClusterScenario.Start, EqualValue),
         new("public-multiblock-offset", TimeSeriesClusterScenario.Start.ToOffset(TimeSpan.FromHours(OffsetHours)), SecondValue),
         new("public-multiblock-late", TimeSeriesClusterScenario.Start.AddTicks(LateTick), -0d),
         .. Enumerable.Range(0, AddedCount).Select(index => new SampleData("public-multiblock-" + index.ToString("D3", CultureInfo.InvariantCulture),
             index % 2 == 0 ? TimeSeriesClusterScenario.Start : TimeSeriesClusterScenario.Start.ToOffset(TimeSpan.FromHours(OffsetHours)), EqualValue))];

    internal static SampleRecord[] Expected(bool healthy)
    {
        var original = Original.Select((sample, index) => new SampleRecord(TimeSeriesClusterScenario.Series,
            sample, index + FirstSequence, TimeSeriesClusterScenario.PublicTags));
        if (healthy)
        {
            original = original.Append(new(TimeSeriesClusterScenario.Series, Fresh, HealthyCount, TimeSeriesClusterScenario.PublicTags));
        }
        return original.OrderBy(row => row.Sample.Timestamp.UtcTicks).ThenBy(row => row.Sequence).ToArray();
    }
}

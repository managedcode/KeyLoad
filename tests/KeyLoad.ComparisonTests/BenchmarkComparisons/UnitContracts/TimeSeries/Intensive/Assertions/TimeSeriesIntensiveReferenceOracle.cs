using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveReferenceOracle
{
    internal const string Tags = "{\"kind\":\"intensive\",\"revision\":1}";
    internal static readonly DateTimeOffset Epoch = new(639028224000000000, TimeSpan.Zero);
    private static readonly ImmutableArray<SampleRecord> Seed = Enumerable.Range(0, 4096).Select(SeedRow)
        .OrderBy(row => row.Sample.Timestamp).ThenBy(row => row.Sequence).ToImmutableArray();

    internal static ImmutableArray<SampleRecord> Raw(int group)
    {
        var from = Epoch.AddMinutes(group * 5 + 1);
        var until = from.AddMinutes(160);
        return Seed.Where(row => row.Sample.Timestamp >= from && row.Sample.Timestamp <= until).ToImmutableArray();
    }

    internal static SampleRecord? Latest(DateTimeOffset cut) => Seed.LastOrDefault(row => row.Sample.Timestamp <= cut);

    internal static ImmutableArray<SampleAggregateWindow> Windows(int group)
    {
        var from = Epoch.AddMinutes(group * 5 + 1);
        var until = from.AddMinutes(160);
        var windows = ImmutableArray.CreateBuilder<SampleAggregateWindow>(54);
        for (var index = 0; index < 54; index++)
        {
            var start = from.AddMinutes(index * 3);
            var end = index == 53 ? until : start.AddMinutes(3);
            windows.Add(new(start, end, Statistics(start, end)));
        }

        return windows.MoveToImmutable();
    }

    internal static SampleRecord Append(int index, string series, long sequence) => new(series,
        new("a-" + index.ToString("D5", CultureInfo.InvariantCulture), Epoch.AddDays(10).AddMilliseconds(index),
            (index + 1729) % 31 - 15 + (index % 16 - 8) / 4d), sequence, Tags);

    internal static Guid CommandId(string run, int repetition, int index)
    {
        var purpose = "measured:" + repetition.ToString(CultureInfo.InvariantCulture) + ":" + index.ToString(CultureInfo.InvariantCulture);
        return new(SHA256.HashData(Encoding.UTF8.GetBytes(run + ":" + purpose)).AsSpan(0, 16));
    }

    private static SampleAggregate Statistics(DateTimeOffset from, DateTimeOffset until)
    {
        var values = Seed.Where(row => row.Sample.Timestamp >= from && row.Sample.Timestamp < until)
            .Select(row => checked((int)(row.Sample.Value * 4))).ToArray();
        return values.Length == 0 ? new(0, 0, null, null, null)
            : new(values.Length, values.Sum() / 4d, values.Min() / 4d, values.Max() / 4d, values.Sum() / (4d * values.Length));
    }

    private static SampleRecord SeedRow(int original) => new("seed",
        new("s-" + original.ToString("D6", CultureInfo.InvariantCulture),
            Epoch.AddMinutes(original / 16 * 5 + original % 16 / 4),
            (original / 16 + 1729) % 31 - 15 + (original % 16 - 8) / 4d), 4096 - original, Tags);
}

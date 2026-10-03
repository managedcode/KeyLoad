using System.Globalization;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveReferencePhases
{
    private const long Epoch = 639028224000000000;

    internal static void Write(TimeSeriesIntensiveReferenceFramer frame)
    {
        frame.Text("phasePlans");
        frame.Count(51280);
        for (var repetition = 0; repetition < 5; repetition++)
        {
            Phase(frame, repetition, true);
            Phase(frame, repetition, false);
        }
    }

    private static void Phase(TimeSeriesIntensiveReferenceFramer frame, int repetition, bool warmup)
    {
        var phase = warmup ? "warmup" : "measured";
        var series = (warmup ? "warm-r" : "measured-r") + repetition.ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < (warmup ? 256 : 10000); index++)
        {
            frame.Field("repetition", repetition);
            frame.Field("phase", phase);
            frame.Field("seriesId", series);
            frame.Field("index", index);
            frame.Field("commandPurpose", phase + ":" + repetition.ToString(CultureInfo.InvariantCulture) + ":" + index.ToString(CultureInfo.InvariantCulture));
            frame.Text("appendSample");
            Append(frame, index);
            Queries(frame, index);
        }
    }

    private static void Append(TimeSeriesIntensiveReferenceFramer frame, int index)
    {
        frame.Field("eventId", "a-" + index.ToString("D5", CultureInfo.InvariantCulture));
        frame.Field("timestampUtcTicks", Epoch + TimeSpan.FromDays(10).Ticks + index * 10000L);
        frame.Field("valueBits", BitConverter.DoubleToInt64Bits((index + 1729) % 31 - 15 + (index % 16 - 8) / 4d));
        frame.Field("tags", "{\"kind\":\"intensive\",\"revision\":1}");
    }

    private static void Queries(TimeSeriesIntensiveReferenceFramer frame, int index)
    {
        var from = Epoch + TimeSpan.FromMinutes(index % 224 * 5 + 1).Ticks;
        var until = from + TimeSpan.FromMinutes(160).Ticks;
        frame.Field("rawFromUtcTicks", from);
        frame.Field("rawUntilUtcTicks", until);
        frame.Field("latestAtOrBeforeUtcTicks", Epoch + TimeSpan.FromMinutes(index % 256 * 5 + 3).Ticks);
        frame.Field("aggregateFromUtcTicks", from);
        frame.Field("aggregateUntilExclusiveUtcTicks", until);
        frame.Field("windowsFromUtcTicks", from);
        frame.Field("windowsUntilExclusiveUtcTicks", until);
    }
}

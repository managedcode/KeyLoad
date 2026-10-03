using System.Globalization;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveReferenceWorkload
{
    private const long Epoch = 639028224000000000;
    private const string Tags = "{\"kind\":\"intensive\",\"revision\":1}";

    internal static string Compute()
    {
        using var frame = new TimeSeriesIntensiveReferenceFramer("keyload.timeseries-intensive.workload");
        Header(frame);
        frame.Text("seedInsertion");
        frame.Count(4096);
        for (var original = 4095; original >= 0; original--)
        {
            var group = original / 16;
            var item = original % 16;
            var row = new SampleRecord("seed", new("s-" + original.ToString("D6", CultureInfo.InvariantCulture),
                new(Epoch + TimeSpan.FromMinutes(group * 5 + item / 4).Ticks, TimeSpan.Zero),
                (group + 1729) % 31 - 15 + (item - 8) / 4d), 4096 - original, Tags);
            TimeSeriesIntensiveReferenceResults.Row(frame, row);
        }

        TimeSeriesIntensiveReferencePhases.Write(frame);
        Readbacks(frame);
        return frame.Finish();
    }

    private static void Header(TimeSeriesIntensiveReferenceFramer frame)
    {
        frame.Field("profile", "intensive-timeseries-4096-c16");
        frame.Field("randomSeed", 1729);
        frame.Field("epochUtcTicks", Epoch);
        frame.Field("sampleCount", 4096);
        frame.Field("operationCount", 10000);
        frame.Field("warmupCount", 256);
        frame.Field("repetitionCount", 5);
        frame.Field("concurrency", 16);
        frame.Field("operationTimeoutTicks", 300000000);
        frame.Field("rawLimit", 1000);
        frame.Field("maxSamples", 10000);
        frame.Field("maxWindows", 1000);
        frame.Field("windowWidthTicks", 1800000000);
        frame.Field("averageToleranceBits", BitConverter.DoubleToInt64Bits(1e-12));
    }

    private static void Readbacks(TimeSeriesIntensiveReferenceFramer frame)
    {
        frame.Text("seedReadbacks");
        frame.Count(16);
        for (var chunk = 0; chunk < 16; chunk++)
        {
            Readback(frame, "seed", Epoch + TimeSpan.FromMinutes(chunk * 80).Ticks,
                Epoch + TimeSpan.FromMinutes(chunk * 80 + 78).Ticks, 256);
        }

        frame.Text("warmupReadbacks");
        frame.Count(5);
        for (var repetition = 0; repetition < 5; repetition++)
        {
            Readback(frame, "warm-r" + repetition.ToString(CultureInfo.InvariantCulture),
                Epoch + TimeSpan.FromDays(10).Ticks, Epoch + TimeSpan.FromDays(10).Ticks + 2550000, 256);
        }

        frame.Text("measuredReadbacks");
        frame.Count(50);
        for (var repetition = 0; repetition < 5; repetition++)
        {
            MeasuredReadbacks(frame, repetition);
        }
    }

    private static void MeasuredReadbacks(TimeSeriesIntensiveReferenceFramer frame, int repetition)
    {
        for (var chunk = 0; chunk < 10; chunk++)
        {
            var from = Epoch + TimeSpan.FromDays(10).Ticks + chunk * 10000000L;
            Readback(frame, "measured-r" + repetition.ToString(CultureInfo.InvariantCulture), from, from + 9990000, 1000);
        }
    }

    private static void Readback(TimeSeriesIntensiveReferenceFramer frame, string series, long from, long until, int count)
    {
        frame.Field("seriesId", series);
        frame.Field("fromUtcTicks", from);
        frame.Field("untilUtcTicks", until);
        frame.Field("expectedCount", count);
    }
}

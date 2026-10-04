using System.Globalization;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveCorpusTests
{
    [Test]
    public async Task AcTsi002SeedMatchesIndependentFormulaAndDescendingInsertion()
    {
        var seed = TimeSeriesIntensiveCorpus.SeedInsertion;
        await Assert.That(seed.Length).IsEqualTo(4096);
        await Assert.That(TimeSeriesIntensiveProfile.Epoch.UtcTicks).IsEqualTo(639028224000000000L);
        for (var ordinal = 0; ordinal < seed.Length; ordinal++)
        {
            var original = 4095 - ordinal;
            var group = original / 16;
            var item = original % 16;
            var row = seed[ordinal];
            await Assert.That(row.SeriesId).IsEqualTo("seed");
            await Assert.That(row.Sample.EventId).IsEqualTo("s-" + original.ToString("D6", CultureInfo.InvariantCulture));
            await Assert.That(row.Sample.Timestamp.UtcTicks).IsEqualTo(639028224000000000L + TimeSpan.FromMinutes(group * 5 + item / 4).Ticks);
            await Assert.That(row.Sample.Value).IsEqualTo((group + 1729) % 31 - 15 + (item - 8) / 4d);
            await Assert.That(row.Sequence).IsEqualTo(ordinal + 1L);
            await Assert.That(row.TagsJson).IsEqualTo("{\"kind\":\"intensive\",\"revision\":1}");
        }

        await Assert.That(seed[0].Sample.Value).IsEqualTo(-13.25);
        await Assert.That(seed[^1].Sample.Value).IsEqualTo(7d);
        await Assert.That(TimeSeriesIntensiveCorpus.SeedBatch(15)[^1]).IsEqualTo(seed[^1].Sample);
    }

    [Test]
    public async Task AcTsi002PlansSeparateLatestGroupAndFreezeInclusiveReadbacks()
    {
        var plan = TimeSeriesIntensivePlans.Read(224);
        await Assert.That(plan.From).IsEqualTo(TimeSeriesIntensiveProfile.Epoch.AddMinutes(1));
        await Assert.That(plan.LatestAtOrBefore).IsEqualTo(TimeSeriesIntensiveProfile.Epoch.AddMinutes(1123));
        await Assert.That(TimeSeriesIntensiveOracle.Latest(plan)!.Sequence).IsEqualTo(500L);
        var seed = TimeSeriesIntensivePlans.SeedReadbacks().ToArray();
        var warm = TimeSeriesIntensivePlans.AppendReadbacks(4, true).ToArray();
        var measured = TimeSeriesIntensivePlans.AppendReadbacks(4, false).ToArray();
        await Assert.That(seed.Length).IsEqualTo(16);
        await Assert.That(warm.Length).IsEqualTo(1);
        await Assert.That(measured.Length).IsEqualTo(10);
        await Assert.That(seed.Sum(item => item.ExpectedCount)).IsEqualTo(4096);
        foreach (var chunk in seed)
        {
            var rows = TimeSeriesIntensiveOracle.SeedReadback(chunk);
            await Assert.That(rows.Length).IsEqualTo(256);
            await Assert.That(rows[0].Sample.Timestamp).IsEqualTo(chunk.From);
            await Assert.That(rows[^1].Sample.Timestamp).IsEqualTo(chunk.Until);
        }

        await Assert.That(seed[0].Until).IsEqualTo(TimeSeriesIntensiveProfile.Epoch.AddMinutes(78));
        await Assert.That(warm[0].Until - warm[0].From).IsEqualTo(TimeSpan.FromMilliseconds(255));
        await Assert.That(measured[0].Until - measured[0].From).IsEqualTo(TimeSpan.FromMilliseconds(999));
        await Assert.That(measured[^1].ExpectedCount).IsEqualTo(1000);
        await Assert.That(TimeSeriesIntensivePlans.WholeSeriesCount("seed")).IsEqualTo(4096);
        await Assert.That(TimeSeriesIntensivePlans.WholeSeriesCount("warm-r4")).IsEqualTo(256);
        await Assert.That(TimeSeriesIntensivePlans.WholeSeriesCount("measured-r4")).IsEqualTo(10000);
    }

    [Test]
    public async Task AcTsi002AppendAndCommandPlansAreInvariantAndRunScoped()
    {
        var sample = TimeSeriesIntensiveCorpus.AppendSample(9999);
        await Assert.That(sample.EventId).IsEqualTo("a-09999");
        await Assert.That(sample.Timestamp).IsEqualTo(TimeSeriesIntensiveProfile.Epoch.AddDays(10).AddMilliseconds(9999));
        await Assert.That(sample.Value).IsEqualTo((9999 + 1729) % 31 - 15 + (9999 % 16 - 8) / 4d);
        await Assert.That(TimeSeriesIntensivePlans.CommandPurpose(4, false, 9999)).IsEqualTo("measured:4:9999");
        var first = TimeSeriesIntensivePlans.CommandId("run-a", "measured:4:9999");
        await Assert.That(first).IsNotEqualTo(TimeSeriesIntensivePlans.CommandId("run-b", "measured:4:9999"));
        await Assert.That(first).IsEqualTo(TimeSeriesIntensivePlans.CommandId("run-a", "measured:4:9999"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensivePlans.CommandPurpose(0, true, 256));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensivePlans.Read(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveCorpus.SeedBatch(16));
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensivePlans.WholeSeriesCount("measured-r5"));
    }
}

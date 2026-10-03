using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveOracleReferenceTests
{
    [Test]
    public async Task AcTsi002EveryRawRowOverAll224RangesMatchesIndependentOrderedRecords()
    {
        for (var group = 0; group < 224; group++)
        {
            var actual = TimeSeriesIntensiveOracle.Raw(TimeSeriesIntensivePlans.Read(group));
            var expected = TimeSeriesIntensiveReferenceOracle.Raw(group);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
    }

    [Test]
    public async Task AcTsi005EveryWindowBoundaryAndStatisticMatchesIndependentReference()
    {
        for (var group = 0; group < 224; group++)
        {
            var actual = TimeSeriesIntensiveOracle.Windows(TimeSeriesIntensivePlans.Read(group));
            var expected = TimeSeriesIntensiveReferenceOracle.Windows(group);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
    }

    [Test]
    public async Task AcTsi005Latest256GroupsAndNarrowTieGapAfterSeedMatchIndependentReference()
    {
        for (var group = 0; group < 256; group++)
        {
            var cut = TimeSeriesIntensiveReferenceOracle.Epoch.AddMinutes(group * 5 + 3);
            await Assert.That(TimeSeriesIntensiveOracle.Latest(cut)).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Latest(cut));
        }

        foreach (var minute in new[] { -1, 1, 4, 5, 1278, 1280 })
        {
            var cut = TimeSeriesIntensiveReferenceOracle.Epoch.AddMinutes(minute);
            await Assert.That(TimeSeriesIntensiveOracle.Latest(cut)).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Latest(cut));
        }
    }

    [Test]
    public async Task AcTsi002EveryAppendIdentityValueAndReceiptSequenceMatchesIndependentReference()
    {
        var receipts = Enumerable.Range(0, 10000).Select(index => new TimeSeriesIntensiveAppendReceipt(
            TimeSeriesIntensiveReferenceOracle.CommandId("reference-run", 4, index), index * 7919L % 10000 + 1)).ToArray();
        TimeSeriesIntensiveAppendOracle.ValidateReceipts("reference-run", 4, false, receipts);
        var plans = TimeSeriesIntensivePlans.AppendReadbacks(4, false).ToArray();
        for (var chunk = 0; chunk < 10; chunk++)
        {
            var actual = TimeSeriesIntensiveAppendOracle.Readback(plans[chunk], receipts);
            var expected = Enumerable.Range(chunk * 1000, 1000).Select(index =>
                TimeSeriesIntensiveReferenceOracle.Append(index, "measured-r4", receipts[index].Sequence));
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
    }
}

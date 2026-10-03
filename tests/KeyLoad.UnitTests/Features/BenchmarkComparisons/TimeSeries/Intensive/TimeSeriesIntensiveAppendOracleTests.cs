using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveAppendOracleTests
{
    [Test]
    public async Task AcTsi002ConcurrentReceiptPermutationDrivesOrderedReadback()
    {
        var receipts = Receipts();
        TimeSeriesIntensiveAppendOracle.ValidateReceipts("run", 0, true, receipts);
        var readback = TimeSeriesIntensivePlans.AppendReadbacks(0, true).Single();
        var expected = TimeSeriesIntensiveAppendOracle.Readback(readback, receipts);
        await Assert.That(expected.Length).IsEqualTo(256);
        await Assert.That(expected[0].Sequence).IsEqualTo(256L);
        await Assert.That(expected[^1].Sequence).IsEqualTo(1L);
        await Assert.That(expected[^1].Sample.EventId).IsEqualTo("a-00255");
        TimeSeriesIntensiveOracle.ValidateRaw(expected, expected);
        TimeSeriesIntensiveOracle.ValidateFullCount("warm-r0", new(256, 0, 0, 0, 0));
    }

    [Test]
    public async Task AcTsi002MissingDuplicateOutOfRangeAndForeignReceiptsFail()
    {
        var receipts = Receipts();
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveAppendOracle.ValidateReceipts("run", 0, true, receipts[..^1]));
        var duplicate = receipts.ToArray();
        duplicate[1] = duplicate[1] with { Sequence = duplicate[0].Sequence };
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveAppendOracle.ValidateReceipts("run", 0, true, duplicate));
        duplicate[1] = receipts[1] with { Sequence = 257 };
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveAppendOracle.ValidateReceipts("run", 0, true, duplicate));
        duplicate[1] = receipts[1] with { CommandId = Guid.Empty };
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveAppendOracle.ValidateReceipts("run", 0, true, duplicate));
        await Assert.That(receipts.Select(row => row.Sequence).Distinct().Count()).IsEqualTo(256);
    }

    [Test]
    public async Task AcTsi002MeasuredReadbacksCoverTenThousandReceiptBoundRowsWithoutTruncation()
    {
        var receipts = Enumerable.Range(0, 10000).Select(index => new TimeSeriesIntensiveAppendReceipt(
            TimeSeriesIntensivePlans.CommandId("measured-run", "measured:4:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            index * 7919L % 10000 + 1)).ToArray();
        TimeSeriesIntensiveAppendOracle.ValidateReceipts("measured-run", 4, false, receipts);
        var total = 0;
        foreach (var plan in TimeSeriesIntensivePlans.AppendReadbacks(4, false))
        {
            var rows = TimeSeriesIntensiveAppendOracle.Readback(plan, receipts);
            await Assert.That(rows.Length).IsEqualTo(1000);
            await Assert.That(rows[0].Sample.Timestamp).IsEqualTo(plan.From);
            await Assert.That(rows[^1].Sample.Timestamp).IsEqualTo(plan.Until);
            total += rows.Length;
        }

        await Assert.That(total).IsEqualTo(10000);
        var invalid = TimeSeriesIntensivePlans.AppendReadbacks(4, false).First() with { ExpectedCount = 1001 };
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveAppendOracle.Readback(invalid, receipts));
    }

    private static TimeSeriesIntensiveAppendReceipt[] Receipts() => Enumerable.Range(0, 256)
        .Select(index => new TimeSeriesIntensiveAppendReceipt(
            TimeSeriesIntensivePlans.CommandId("run", "warmup:0:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)), 256 - index))
        .ToArray();
}

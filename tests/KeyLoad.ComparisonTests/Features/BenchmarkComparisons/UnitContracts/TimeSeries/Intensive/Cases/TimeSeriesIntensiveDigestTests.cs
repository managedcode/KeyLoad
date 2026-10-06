using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveDigestTests
{
    [Test]
    public async Task AcTsi002ResultFramesMatchIndependentBigEndianReference()
    {
        ImmutableArray<SampleRecord> rows = [new("seed", new("e-\u00e9", new(639028224000000000, TimeSpan.Zero), -13.25), 1, "{\"kind\":\"intensive\",\"revision\":1}")];
        var aggregate = new SampleAggregate(1, -13.25, -13.25, -13.25, -13.25);
        ImmutableArray<SampleAggregateWindow> windows = [new(rows[0].Sample.Timestamp, null, aggregate)];
        var receipt = new TimeSeriesIntensiveAppendReceipt(new("00112233-4455-6677-8899-aabbccddeeff"), 42);
        await Assert.That(TimeSeriesIntensiveResultDigest.Raw(rows)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Raw(rows));
        await Assert.That(TimeSeriesIntensiveResultDigest.Latest(rows[0])).IsEqualTo(TimeSeriesIntensiveReferenceResults.Latest(rows[0]));
        await Assert.That(TimeSeriesIntensiveResultDigest.Latest(null)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Latest(null));
        await Assert.That(TimeSeriesIntensiveResultDigest.Aggregate(aggregate)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Aggregate(aggregate));
        await Assert.That(TimeSeriesIntensiveResultDigest.Aggregate(new(0, 0, null, null, null))).IsEqualTo(TimeSeriesIntensiveReferenceResults.Aggregate(new(0, 0, null, null, null)));
        await Assert.That(TimeSeriesIntensiveResultDigest.Windows(windows)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Windows(windows));
        await Assert.That(TimeSeriesIntensiveResultDigest.AppendReceipt(receipt)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Receipt(receipt.CommandId, receipt.Sequence));
    }

    [Test]
    public async Task AcTsi002DigestPreservesOrderUtcCanonicalTagsAndActualAverageBits()
    {
        var rows = TimeSeriesIntensiveOracle.Raw(TimeSeriesIntensivePlans.Read(0));
        var equivalent = rows.SetItem(0, rows[0] with
        {
            Sample = rows[0].Sample with { Timestamp = rows[0].Sample.Timestamp.ToOffset(TimeSpan.FromHours(-5)) },
            TagsJson = "{\"revision\":1,\"kind\":\"intensive\"}"
        });
        await Assert.That(TimeSeriesIntensiveResultDigest.Raw(rows)).IsEqualTo(TimeSeriesIntensiveResultDigest.Raw(equivalent));
        await Assert.That(TimeSeriesIntensiveResultDigest.Raw(rows)).IsNotEqualTo(TimeSeriesIntensiveResultDigest.Raw(rows.Reverse().ToImmutableArray()));
        var aggregate = TimeSeriesIntensiveOracle.Aggregate(TimeSeriesIntensivePlans.Read(0));
        await Assert.That(TimeSeriesIntensiveResultDigest.Aggregate(aggregate)).IsNotEqualTo(TimeSeriesIntensiveResultDigest.Aggregate(aggregate with { Average = aggregate.Average + 5e-13 }));
    }

    [Test]
    public async Task AcTsi002WholeWorkloadMatchesIndependentFramingAcrossCultureAndRunIds()
    {
        var expected = TimeSeriesIntensiveReferenceWorkload.Compute();
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            await Assert.That(TimeSeriesIntensiveWorkloadDigest.Compute(UnitBenchmarkOptions.Native())).IsEqualTo(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        await Assert.That(TimeSeriesIntensivePlans.CommandId("run-a", "warmup:0:0")).IsNotEqualTo(TimeSeriesIntensivePlans.CommandId("run-b", "warmup:0:0"));
        await Assert.That(TimeSeriesIntensiveWorkloadDigest.Compute(UnitBenchmarkOptions.Native())).IsEqualTo(expected);
        await Assert.That(expected.Length).IsEqualTo(64);
    }

    [Test]
    public async Task AcCq034ConfiguredOperationTimeoutChangesOnlyItsIndependentWorkloadFrameAsync()
    {
        const long configuredTicks = 7 * TimeSpan.TicksPerSecond;
        var execution = UnitBenchmarkOptions.Native();
        execution.Value.OperationTimeout = TimeSpan.FromTicks(configuredTicks);
        execution.Value.Validate();
        var expected = TimeSeriesIntensiveReferenceWorkload.Compute(configuredTicks);
        var actual = TimeSeriesIntensiveWorkloadDigest.Compute(execution);
        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(actual).IsNotEqualTo(TimeSeriesIntensiveReferenceWorkload.Compute());
    }
}

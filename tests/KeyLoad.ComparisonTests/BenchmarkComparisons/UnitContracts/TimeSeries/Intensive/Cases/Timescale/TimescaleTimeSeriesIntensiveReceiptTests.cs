using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveReceiptTests
{
    private const string RunId = "timescale-adapter-run";

    [Test]
    public async Task AcTsi002ScalarReceiptPreservesTheActualCommandAndNonOrdinalSequence()
    {
        var commandId = TimeSeriesIntensivePlans.CommandId(RunId, "append:sample-1");
        var actual = new TimeSeriesIntensiveAppendReceipt(commandId, 9876);
        var validated = TimescaleTimeSeriesIntensiveReceipt.ValidateScalar(actual, commandId);
        await Assert.That(validated).IsSameReferenceAs(actual);
        await Assert.That(validated.CommandId).IsEqualTo(commandId);
        await Assert.That(validated.Sequence).IsEqualTo(9876L);
    }

    [Test]
    public async Task AcTsi002ScalarReceiptRejectsAbsentWrongAndNonpositiveActualFacts()
    {
        var commandId = TimeSeriesIntensivePlans.CommandId(RunId, "append:sample-2");
        var invalid = new TimeSeriesIntensiveAppendReceipt?[]
        {
            null,
            new(Guid.Empty, 1),
            new(Guid.NewGuid(), 1),
            new(commandId, 0),
            new(commandId, -1)
        };

        foreach (var receipt in invalid)
        {
            Assert.ThrowsExactly<ComparisonFailureException>(() =>
                TimescaleTimeSeriesIntensiveReceipt.ValidateScalar(receipt, commandId));
        }

        await Assert.That(invalid.Length).IsEqualTo(5);
    }

    [Test]
    public async Task AcTsi002AllSixteenSeedBatchesRequireExactOrdinalSequenceAndCommandEcho()
    {
        var receipts = ImmutableArray.CreateBuilder<TimeSeriesIntensiveAppendReceipt>(16);
        for (var batch = 0; batch < TimeSeriesIntensiveProfile.SeedBatchCount; batch++)
        {
            var commandId = IndependentSeedCommandId(batch);
            await Assert.That(TimescaleTimeSeriesIntensiveTarget.SeedCommandId(RunId, batch)).IsEqualTo(commandId);
            var rows = BatchRows(batch, commandId);
            var receipt = TimescaleTimeSeriesIntensiveReceipt.ValidateBatch(rows, commandId, batch);
            receipts.Add(receipt);
            await Assert.That(receipt.CommandId).IsEqualTo(commandId);
            await Assert.That(receipt.Sequence).IsEqualTo((batch + 1L) * TimeSeriesIntensiveProfile.SeedBatchSize);
        }

        await Assert.That(receipts.Select(receipt => receipt.CommandId).Distinct().Count()).IsEqualTo(16);
        await Assert.That(receipts[^1].Sequence).IsEqualTo(4096L);
    }

    [Test]
    public async Task AcTsi002SeedReceiptRejectsMissingExtraMisorderedOrInventedRows()
    {
        var commandId = IndependentSeedCommandId(4);
        var rows = BatchRows(4, commandId);
        var invalid = new[]
        {
            ImmutableArray<TimescaleTimeSeriesIntensiveAppendRow>.Empty,
            rows.RemoveAt(rows.Length - 1),
            rows.Add(rows[^1]),
            rows.SetItem(0, rows[0] with { Ordinal = 2 }),
            rows.SetItem(0, rows[0] with { CommandId = Guid.NewGuid() }),
            rows.SetItem(0, rows[0] with { Sequence = 1 }),
            rows.SetItem(0, rows[0] with { Sequence = 0 })
        };

        foreach (var candidate in invalid)
        {
            Assert.ThrowsExactly<ComparisonFailureException>(() =>
                TimescaleTimeSeriesIntensiveReceipt.ValidateBatch(candidate, commandId, 4));
        }

        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            TimescaleTimeSeriesIntensiveReceipt.ValidateBatch(default, commandId, 4));
        await Assert.That(invalid.Length).IsEqualTo(7);
    }

    private static ImmutableArray<TimescaleTimeSeriesIntensiveAppendRow> BatchRows(int batch, Guid commandId)
    {
        var rows = ImmutableArray.CreateBuilder<TimescaleTimeSeriesIntensiveAppendRow>(TimeSeriesIntensiveProfile.SeedBatchSize);
        var sequenceOffset = (long)batch * TimeSeriesIntensiveProfile.SeedBatchSize;
        for (var ordinal = 1; ordinal <= TimeSeriesIntensiveProfile.SeedBatchSize; ordinal++)
        {
            rows.Add(new(ordinal, commandId, sequenceOffset + ordinal));
        }

        return rows.MoveToImmutable();
    }

    private static Guid IndependentSeedCommandId(int batch) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(RunId + ":seed:b"
            + batch.ToString(System.Globalization.CultureInfo.InvariantCulture))).AsSpan(0, 16));
}

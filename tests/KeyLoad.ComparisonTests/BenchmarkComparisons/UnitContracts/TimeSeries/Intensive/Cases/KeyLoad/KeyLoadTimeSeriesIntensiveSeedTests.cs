using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class KeyLoadTimeSeriesIntensiveSeedTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AcTsi002IndependentDescendingSeedFormulaCoversAllBatchTerminals()
    {
        for (var batch = 0; batch < 16; batch++)
        {
            var actual = TimeSeriesIntensiveCorpus.SeedBatch(batch);
            await Assert.That(actual.Length).IsEqualTo(256);
            for (var offset = 0; offset < actual.Length; offset++)
            {
                var ordinal = batch * 256 + offset;
                var original = 4095 - ordinal;
                var group = original / 16;
                var item = original % 16;
                var expectedTime = Epoch.AddMinutes(group * 5 + item / 4);
                var expectedValue = ((group + 1729) % 31 - 15) + (item - 8) / 4d;
                await Assert.That(actual[offset].EventId).IsEqualTo(
                    "s-" + original.ToString("D6", CultureInfo.InvariantCulture));
                await Assert.That(actual[offset].Timestamp.UtcTicks).IsEqualTo(expectedTime.UtcTicks);
                await Assert.That(actual[offset].Value).IsEqualTo(expectedValue);
                await Assert.That(double.IsFinite(expectedValue)).IsTrue();
            }

            var observedBatchTerminalSequence = batch * 256L + actual.Length;
            await Assert.That(observedBatchTerminalSequence).IsEqualTo((batch + 1L) * 256);
        }
    }

    [Test]
    public async Task AcTsi002SeedBatchCommandIdentitiesAreStableAndDistinct()
    {
        var actual = Enumerable.Range(0, 16)
            .Select(batch => KeyLoadTimeSeriesIntensiveTarget.SeedCommandId("intensive-run", batch))
            .ToArray();
        var expected = Enumerable.Range(0, 16)
            .Select(batch => IndependentCommandId("intensive-run", "seed:" + batch.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(actual.Distinct().Count()).IsEqualTo(16);
        await Assert.That(KeyLoadTimeSeriesIntensiveTarget.SeedCommandId("intensive-run", 0))
            .IsEqualTo(actual[0]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KeyLoadTimeSeriesIntensiveTarget.SeedCommandId("intensive-run", 16));
    }

    private static Guid IndependentCommandId(string runId, string purpose)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(runId + ":" + purpose));
        return new(hash.AsSpan(0, 16));
    }
}

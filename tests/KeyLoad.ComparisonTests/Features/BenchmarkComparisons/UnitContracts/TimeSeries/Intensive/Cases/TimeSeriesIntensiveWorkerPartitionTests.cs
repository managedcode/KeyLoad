using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveWorkerPartitionTests
{
    [Test]
    public async Task AcTsi004SixteenMeasuredWorkersCoverEachIndexExactlyOnce()
        => await VerifyAsync(10000, 625);

    [Test]
    public async Task AcTsi004SixteenWarmupWorkersCoverEachIndexExactlyOnce()
        => await VerifyAsync(256, 16);

    private static async Task VerifyAsync(int count, int expectedPerWorker)
    {
        var seen = new int[count];
        for (var worker = 0; worker < 16; worker++)
        {
            var indices = TimeSeriesIntensiveWorkerPartition.Indices(worker, count).ToArray();
            await Assert.That(indices.Length).IsEqualTo(expectedPerWorker);
            await Assert.That(indices.SequenceEqual(Enumerable.Range(0, expectedPerWorker).Select(position => worker + position * 16))).IsTrue();
            foreach (var index in indices)
            {
                seen[index]++;
            }
        }

        await Assert.That(seen.All(visits => visits == 1)).IsTrue();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveWorkerPartition.Indices(16, count));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveWorkerPartition.Indices(-1, count));
    }
}

using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaledLatencySampleTests
{
    [Test]
    public async Task SampleIndicesMatchTheFrozenEvenSpacingFormulaAndCapacity()
    {
        const int operations = 100_000;
        const int capacity = 4_096;
        var indices = ScaledLatencySample.Indices(operations, capacity);
        await Assert.That(indices.Length).IsEqualTo(capacity);
        await Assert.That(indices[0]).IsEqualTo(0);
        await Assert.That(indices[^1]).IsEqualTo(operations - 1);
        for (var index = 0; index < capacity; index++)
        {
            await Assert.That(indices[index]).IsEqualTo((int)((long)index * (operations - 1) / (capacity - 1)));
        }
        var accounting = new ScaledOperationAccounting(operations, operations, operations, 0, 0, 0, 0,
            "evenly-spaced-operation-indices.v1", capacity, capacity, 0);
        await Assert.That(accounting.LatencyQuantileMethod).IsEqualTo("sampled-estimate");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScaledLatencySample.Indices(4_095, capacity));
    }
}

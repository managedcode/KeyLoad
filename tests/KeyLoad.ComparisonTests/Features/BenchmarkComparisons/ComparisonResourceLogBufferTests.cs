namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonResourceLogBufferTests
{
    /// <summary>AC-ISO-006: independent resources retain bounded native lines and truncate oversized messages.</summary>
    [Test]
    public async Task NativeResourceLogsHaveIndependentLineByteAndMessageBounds()
    {
        var first = new ComparisonResourceLogBuffer(3, 20, 8);
        first.Add("one");
        first.Add("two");
        first.Add("three");
        first.Add("four");
        await Assert.That(first.Snapshot()).IsEquivalentTo(new[] { "two", "three", "four" });
        first.Add(new string('x', 100));
        await Assert.That(first.Snapshot().Sum(line => System.Text.Encoding.UTF8.GetByteCount(line) + 1))
            .IsLessThanOrEqualTo(20);
        await Assert.That(first.Snapshot().Last().Length).IsEqualTo(8);
        var second = new ComparisonResourceLogBuffer(3, 20, 8);
        second.Add("node-two");
        await Assert.That(second.Snapshot()).IsEquivalentTo(new[] { "node-two" });
        await Assert.That(first.Snapshot()).DoesNotContain("node-two");
        second.Add("🙂🙂🙂🙂🙂");
        await Assert.That(second.Snapshot().Last()).IsEqualTo("🙂🙂");
    }
}

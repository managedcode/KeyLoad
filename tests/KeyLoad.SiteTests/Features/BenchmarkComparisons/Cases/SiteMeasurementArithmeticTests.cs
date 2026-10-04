using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteMeasurementArithmeticTests
{
    private static readonly double[] OddValues = [9, 2, 5];
    private static readonly double[] EvenValues = [8, 2, 6, 4];
    private static readonly double[] EmptyValues = [];

    [Test]
    public async Task SharedMedianPreservesOddEvenAndEmptyInputSemantics()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var odd = await SiteNodeProbe.RunAsync(inputs, new(SiteAssetTokens.MedianOperation, OddValues), token);
        var even = await SiteNodeProbe.RunAsync(inputs, new(SiteAssetTokens.MedianOperation, EvenValues), token);
        var empty = await SiteNodeProbe.RunAsync(inputs, new(SiteAssetTokens.MedianOperation, EmptyValues), token);

        await Assert.That(odd.Value.ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(odd.Value.GetDouble()).IsEqualTo(5d);
        await Assert.That(even.Value.ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(even.Value.GetDouble()).IsEqualTo(5d);
        await Assert.That(empty.Value.ValueKind).IsEqualTo(JsonValueKind.Null);
    }
}

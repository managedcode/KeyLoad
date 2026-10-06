using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonCorpusTests
{
    private const string OriginalPropertyOrderJson = "{\"id\":1,\"payload\":\"x\"}";
    private const string ReorderedPropertyJson = "{\"payload\":\"x\",\"id\":1}";
    private const string OriginalIdentifierJson = "{\"id\":1}";
    private const string DifferentIdentifierJson = "{\"id\":2}";

    [Test]
    public async Task CorpusIsByteExactAndReproducibleAcrossTargetsAndSeeds()
    {
        var a = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small));
        var b = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small));
        await Assert.That(b.Sha256).IsEqualTo(a.Sha256);
        await Assert.That(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small with { Seed = ComparisonHarnessInputs.Small.Seed + 1 })).Sha256).IsNotEqualTo(a.Sha256);
        foreach (var document in a.Documents)
        {
            await Assert.That(System.Text.Encoding.UTF8.GetByteCount(document.Json)).IsEqualTo(ComparisonHarnessInputs.Small.PayloadBytes);
        }
        foreach (var document in a.Documents)
        {
            await Assert.That(a.ExactNeighbors(document)[0].Id).IsEqualTo(document.Id);
        }
        await Assert.That(b.Documents[0].Json).IsEqualTo(a.Documents[0].Json);
        await Assert.That(BenchmarkDataset.SameJson(OriginalPropertyOrderJson, ReorderedPropertyJson)).IsTrue();
        await Assert.That(BenchmarkDataset.SameJson(OriginalIdentifierJson, DifferentIdentifierJson)).IsFalse();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small with { TopK = 17 })));
    }
}

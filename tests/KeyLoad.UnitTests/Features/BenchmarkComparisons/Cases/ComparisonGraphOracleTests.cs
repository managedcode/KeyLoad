using KeyLoad.Comparisons;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonGraphOracleTests
{
    [Test]
    public async Task GraphOracleHandlesCyclesDepthAndDisconnectedComponents()
    {
        var data = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small));
        await Assert.That(data.Reachable(data.Documents[0], 1)).IsEquivalentTo(data.Documents[1..4].Select(document => document.Id), CollectionOrdering.Matching);
        await Assert.That(data.Reachable(data.Documents[0], 2)).IsEquivalentTo(data.Documents[1..7].Select(document => document.Id), CollectionOrdering.Matching);
        await Assert.That(data.Reachable(data.Documents[0], 5)).IsEquivalentTo(data.Documents[1..8].Select(document => document.Id), CollectionOrdering.Matching);
        await Assert.That(data.Reachable(data.Documents[0], 5)).DoesNotContain(id => id == data.Documents[8].Id);
        await Assert.That(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small with { GraphFanOut = 2 })).Sha256).IsNotEqualTo(data.Sha256);
        await Assert.That(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small with { Documents = 1, TopK = 1 })).Edges).IsEmpty();
    }
}

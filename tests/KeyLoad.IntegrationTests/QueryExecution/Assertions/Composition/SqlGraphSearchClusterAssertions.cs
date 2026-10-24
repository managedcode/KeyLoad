using KeyLoad.IntegrationTests.Search;

namespace KeyLoad.IntegrationTests.QueryExecution;

/// <summary>Verifies SQL graph-search output against direct graph-search semantics.</summary>
internal static class SqlGraphSearchClusterAssertions
{
    internal static async Task AssertExpectedAsync(GraphSearchResult result)
    {
        await GraphSearchClusterAssertions.AssertHitsAsync(result,
            (GraphSearchClusterScenario.Alpha, 1d / (GraphSearchClusterScenario.FusionConstant + 1)),
            (GraphSearchClusterScenario.Beta, 1d / (GraphSearchClusterScenario.FusionConstant + 2)),
            (GraphSearchClusterScenario.Gamma, 1d / (GraphSearchClusterScenario.FusionConstant + 3)));
        await Assert.That(result.Expansion).IsNull();
    }
}

using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Verifies SQL graph-search output against direct graph-search semantics.</summary>
internal static class SqlGraphSearchRf3Assertions
{
    internal static async Task AssertExpectedAsync(GraphSearchResult result)
    {
        await GraphSearchRf3Assertions.AssertHitsAsync(result,
            (GraphSearchRf3Scenario.Alpha, 1d / (GraphSearchRf3Scenario.FusionConstant + 1)),
            (GraphSearchRf3Scenario.Beta, 1d / (GraphSearchRf3Scenario.FusionConstant + 2)),
            (GraphSearchRf3Scenario.Gamma, 1d / (GraphSearchRf3Scenario.FusionConstant + 3)));
        await Assert.That(result.Expansion).IsNull();
    }
}

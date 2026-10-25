namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class QueryRuleOptimizationWholeFlowTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task MetadataRevisionIndexPlansKeepResidualRowsAuthorityReplayAndTwoCold(bool includeNull, bool includeMissing)
        => QueryRuleOptimizationFlow.RunAsync(includeNull, includeMissing, TestContext.Current!.Execution.CancellationToken);
}

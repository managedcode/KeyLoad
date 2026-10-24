namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class QueryRuleOptimizationClusterTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task MetadataRevisionIndexPlansKeepPublicResultsFreshUseReplayAndTwoCold(bool includeNull, bool includeMissing)
        => QueryRuleOptimizationClusterFlow.RunAsync(includeNull, includeMissing);
}

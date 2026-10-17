namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class QueryRuleOptimizationRf3Tests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task MetadataRevisionIndexPlansKeepPublicResultsFreshUseReplayAndTwoCold(bool includeNull, bool includeMissing)
        => QueryRuleOptimizationRf3Flow.RunAsync(includeNull, includeMissing);
}

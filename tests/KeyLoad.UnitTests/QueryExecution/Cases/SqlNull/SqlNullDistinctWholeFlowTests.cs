namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlNullDistinctWholeFlowTests
{
    [Test]
    public Task NullDistinctComparisonsRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlNullDistinctFlow.RunAsync();
}

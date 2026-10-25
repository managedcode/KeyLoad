namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlNullDistinctWholeFlowTests
{
    [Test]
    public Task NullDistinctComparisonsRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlNullDistinctFlow.RunAsync();
}

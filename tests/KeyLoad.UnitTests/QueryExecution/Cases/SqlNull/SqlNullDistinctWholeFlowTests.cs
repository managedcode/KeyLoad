namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlNullDistinctWholeFlowTests
{
    [Test]
    public Task NullDistinctComparisonsRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlNullDistinctFlow.RunAsync();
}

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlBooleanTruthWholeFlowTests
{
    [Test]
    public Task BooleanTruthPredicatesRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBooleanTruthFlow.RunAsync();
}

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlBooleanTruthWholeFlowTests
{
    [Test]
    public Task BooleanTruthPredicatesRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBooleanTruthFlow.RunAsync();
}

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlBetweenSymmetricWholeFlowTests
{
    [Test]
    public Task BetweenSymmetricPredicatesRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBetweenSymmetricFlow.RunAsync();
}

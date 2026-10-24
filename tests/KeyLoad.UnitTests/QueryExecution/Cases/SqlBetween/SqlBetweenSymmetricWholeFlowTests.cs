namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlBetweenSymmetricWholeFlowTests
{
    [Test]
    public Task BetweenSymmetricPredicatesRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBetweenSymmetricFlow.RunAsync();
}

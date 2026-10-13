namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlBetweenSymmetricWholeFlowTests
{
    [Test]
    public Task BetweenSymmetricPredicatesRetainMissingUnknownWholeSqlAstRefusalTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBetweenSymmetricFlow.RunAsync();
}

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlNullOrderingWholeFlowTests
{
    [Test]
    public Task ExplicitNullPlacementRetainsWholeSqlAstPagesAuthorityNoEffectsReplayAndTwoColdCuts()
        => SqlNullOrderingFlow.RunAsync();
}

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlNullOrderingWholeFlowTests
{
    [Test]
    public Task ExplicitNullPlacementRetainsWholeSqlAstPagesAuthorityNoEffectsReplayAndTwoColdCuts()
        => SqlNullOrderingFlow.RunAsync();
}

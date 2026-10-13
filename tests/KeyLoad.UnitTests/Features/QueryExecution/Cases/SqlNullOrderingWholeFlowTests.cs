namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlNullOrderingWholeFlowTests
{
    [Test]
    public Task ExplicitNullPlacementRetainsWholeSqlAstPagesAuthorityNoEffectsReplayAndTwoColdCuts()
        => SqlNullOrderingFlow.RunAsync();
}

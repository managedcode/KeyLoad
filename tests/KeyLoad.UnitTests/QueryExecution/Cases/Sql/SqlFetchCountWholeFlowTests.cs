namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlFetchCountWholeFlowTests
{
    [Test]
    public Task FetchCountRetainsWholeSqlAstPagesAuthorityNoEffectsReplayAndTwoColdCuts()
        => SqlFetchCountFlow.RunAsync();
}

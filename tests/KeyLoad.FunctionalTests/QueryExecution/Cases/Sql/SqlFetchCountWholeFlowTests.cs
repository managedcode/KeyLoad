namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlFetchCountWholeFlowTests
{
    [Test]
    public Task FetchCountRetainsWholeSqlAstPagesAuthorityNoEffectsReplayAndTwoColdCuts()
        => SqlFetchCountFlow.RunAsync();
}

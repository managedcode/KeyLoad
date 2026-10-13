namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlFetchCountWholeFlowTests
{
    [Test]
    public Task FetchCountRetainsWholeSqlAstPagesAuthorityNoEffectsReplayAndTwoColdCuts()
        => SqlFetchCountFlow.RunAsync();
}

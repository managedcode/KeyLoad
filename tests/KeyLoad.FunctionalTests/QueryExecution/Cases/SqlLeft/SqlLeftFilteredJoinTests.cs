namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlLeftFilteredJoinTests
{
    [Test]
    public async Task LeftPredicateJoinRefusesThenRepairsLiteralParameterizedPagesAndReplaysAcrossTwoColdCuts()
    {
        using var database = new TestDatabase();
        var seed = SqlLeftFilteredJoinFixture.Seed(database);
        await SqlLeftFilteredJoinOperation.VerifyAsync(database);
        await SqlLeftFilteredJoinOperation.RefusalsAsync(database);
        await SqlLeftFilteredJoinOperation.PolicyAsync(database);
        await SqlLeftFilteredJoinCold.RunAsync(database, seed.Command, seed.Receipt);
    }
}

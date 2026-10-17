namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlLeftFilteredJoinTests
{
    [Test]
    public async Task AcKl095LeftPredicateJoinRefusesThenRepairsLiteralParameterizedPagesAndReplaysAcrossTwoColdCuts()
    {
        using var database = new TestDatabase();
        var seed = SqlLeftFilteredJoinFixture.Seed(database);
        await SqlLeftFilteredJoinOperation.VerifyAsync(database);
        await SqlLeftFilteredJoinOperation.RefusalsAsync(database);
        await SqlLeftFilteredJoinOperation.PolicyAsync(database);
        await SqlLeftFilteredJoinBudget.RunAsync(database);
        await SqlLeftFilteredJoinCold.RunAsync(database, seed.Command, seed.Receipt);
    }
}

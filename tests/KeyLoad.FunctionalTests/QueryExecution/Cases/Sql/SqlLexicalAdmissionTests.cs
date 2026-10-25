namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlLexicalAdmissionTests
{
    [Test]
    public async Task StringAndParameterTokensCannotReplaceGrammarAndRepairReplaysAcrossTwoColdOwners()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlJoinSourceBindingFixture.Seed(database, SqlLexicalAdmissionFixture.Kind);
                await SqlLexicalAdmissionFlow.RunAsync(database.Database, database.Store, database.Partition, seed.Command, seed.Receipt);
                await SqlLexicalAdmissionCold.RunAsync(database, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

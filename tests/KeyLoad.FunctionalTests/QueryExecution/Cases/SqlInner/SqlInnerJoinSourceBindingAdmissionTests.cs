namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlInnerJoinSourceBindingAdmissionTests
{
    [Test]
    [Arguments(SqlJoinSourceBindingFixture.Typed)]
    [Arguments(SqlJoinSourceBindingFixture.Queue)]
    [Arguments(SqlJoinSourceBindingFixture.Events)]
    public async Task UnboundDeclaredProjectionRefusesBeforeReadsThenRepairsReplaysAcrossTwoColdOwners(string kind)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlJoinSourceBindingFixture.Seed(database, kind);
                await SqlJoinSourceBindingFlow.RunAsync(database.Database, database.Store, database.Partition, kind, seed.Command, seed.Receipt);
                await SqlJoinSourceBindingCold.RunAsync(database, kind, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

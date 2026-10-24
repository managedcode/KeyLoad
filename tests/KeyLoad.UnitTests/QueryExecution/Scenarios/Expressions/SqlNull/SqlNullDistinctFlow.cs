using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlNullDistinctFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlNullOrderingFixture.Seed(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.InitialEpoch, true);
                await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store,
                    database.Partition, SqlNullDistinctRefusals.InitialRevision);
                await SqlNullDistinctRefusals.InvalidAsync(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.DeniedEpoch, false);
                var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
                var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
                var position = database.Store.Position;
                var identity = JsonDefaults.Serialize(database.Store.Identity);
                foreach (var item in SqlNullDistinctCases.All)
                {
                    await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                        new(database.Partition, SqlNullDistinctCases.Prefix + item.Clause + SqlNullDistinctCases.Tail,
                            AllowFullScan: true))).Code).IsEqualTo(ErrorCode.PermissionDenied);
                    await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                        SqlNullDistinctCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.PermissionDenied);
                }
                await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.RepairedEpoch, true);
                await SqlNullDistinctRefusals.TypeRepairAsync(database);
                await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store,
                    database.Partition, SqlNullDistinctRefusals.RepairedRevision);
                await SqlNullOrderingPages.ReplayAsync(database.Database, seed.Command, seed.Receipt);
                await SqlNullDistinctCold.RunAsync(database, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBooleanTruthFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlBooleanTruthFixture.Seed(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.InitialEpoch, true);
                await SqlBooleanTruthPages.HealthyAsync(database.Database, database.Store,
                    database.Partition, SqlBooleanTruthRefusals.InitialRevision);
                await SqlBooleanTruthRefusals.InvalidAsync(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.DeniedEpoch, false);
                var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
                var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
                var position = database.Store.Position;
                var identity = JsonDefaults.Serialize(database.Store.Identity);
                foreach (var item in SqlBooleanTruthCases.All)
                {
                    await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                        new(database.Partition, SqlBooleanTruthCases.Prefix + item.Clause + SqlBooleanTruthCases.Tail,
                            AllowFullScan: true))).Code).IsEqualTo(ErrorCode.PermissionDenied);
                    await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                        SqlBooleanTruthCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.PermissionDenied);
                }
                await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.RepairedEpoch, true);
                await SqlBooleanTruthRefusals.TypeRepairAsync(database);
                await SqlBooleanTruthPages.HealthyAsync(database.Database, database.Store,
                    database.Partition, SqlBooleanTruthRefusals.RepairedRevision);
                await SqlNullOrderingPages.ReplayAsync(database.Database, seed.Command, seed.Receipt);
                await SqlBooleanTruthCold.RunAsync(database, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

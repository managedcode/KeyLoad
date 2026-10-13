using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlBetweenSymmetricFixture.Seed(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.InitialEpoch, true);
                await SqlBetweenSymmetricPages.HealthyAsync(database.Database, database.Store,
                    database.Partition, SqlBetweenSymmetricRefusals.InitialRevision);
                await SqlBetweenSymmetricRefusals.InvalidAsync(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.DeniedEpoch, false);
                var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
                var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
                var position = database.Store.Position;
                var identity = JsonDefaults.Serialize(database.Store.Identity);
                foreach (var item in SqlBetweenSymmetricCases.All)
                {
                    await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                        new(database.Partition, SqlBetweenSymmetricCases.Prefix + item.Clause + SqlBetweenSymmetricCases.Tail,
                            AllowFullScan: true))).Code).IsEqualTo(ErrorCode.PermissionDenied);
                    await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                        SqlBetweenSymmetricCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.PermissionDenied);
                }
                await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.RepairedEpoch, true);
                await SqlBetweenSymmetricRefusals.TypeRepairAsync(database);
                await SqlBetweenSymmetricPages.HealthyAsync(database.Database, database.Store,
                    database.Partition, SqlBetweenSymmetricRefusals.RepairedRevision);
                await SqlNullOrderingPages.ReplayAsync(database.Database, seed.Command, seed.Receipt);
                await SqlBetweenSymmetricCold.RunAsync(database, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

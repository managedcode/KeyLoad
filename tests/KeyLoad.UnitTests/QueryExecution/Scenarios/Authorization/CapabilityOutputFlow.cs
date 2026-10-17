using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class CapabilityOutputFlow
{
    internal static async Task RunAsync(int mode, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(new DatabaseLimits
            {
                MaxBatchBytes = CapabilityOutputFixture.NativeBytes,
                MaxQueryBytes = CapabilityOutputFixture.RequestBytes
            });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var seeds = CapabilityOutputFixture.Seed(database);
                CapabilityOutputFixture.Policy(database, true, 1);
                var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution(CapabilityOutputFixture.Options(mode)));
                await CapabilityOutputOperations.RunAsync(database.Store, database.Partition, engine, mode, token);
                await DenyCancelAsync(database, engine);
                await CapabilityOutputOperations.RunAsync(database.Store, database.Partition, engine, mode, token);
                await CapabilityOutputOperations.ReplayAsync(database.Database, seeds);
                await CapabilityOutputCold.RunAsync(database, seeds, mode, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DenyCancelAsync(TestDatabase database, QueryEngine engine)
    {
        var beforeCancellation = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var beforePosition = database.Store.Position;
        var beforeIdentity = NativeSerialization.Serialize(database.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var sql = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(CapabilityOutputFixture.Reader,
                    CapabilityOutputFixture.Sql(database.Partition, true, 1), cancellationToken: cancelled.Token));
                var ast = Assert.ThrowsExactly<OperationCanceledException>(() => engine.ExecuteAst(CapabilityOutputFixture.Reader,
                    CapabilityOutputFixture.Ast(database.Partition, true, 1), cancellationToken: cancelled.Token));
                await Assert.That(sql.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(ast.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(database.Store,
            beforeCancellation, beforePosition, beforeIdentity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        CapabilityOutputFixture.Policy(database, false, 2);
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var deniedIdentity = NativeSerialization.Serialize(database.Store.Identity);
        await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.RejectAsync(() => engine.Execute(
            CapabilityOutputFixture.Reader, CapabilityOutputFixture.Sql(database.Partition, true, 1)),
            ErrorCode.PermissionDenied), failures);
        await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.RejectAsync(() => engine.ExecuteAst(
            CapabilityOutputFixture.Reader, CapabilityOutputFixture.Ast(database.Partition, true, 1)),
            ErrorCode.PermissionDenied), failures);
        await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(database.Store,
            image, position, deniedIdentity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        CapabilityOutputFixture.Policy(database, true, 3);
    }
}

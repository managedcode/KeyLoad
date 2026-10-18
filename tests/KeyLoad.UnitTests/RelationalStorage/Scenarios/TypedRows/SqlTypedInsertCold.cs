using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertCold
{
    internal static async Task RunAsync(TestDatabase fixture, SqlTypedInsertState state, CancellationToken token)
    {
        var identity = fixture.Store.Identity;
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        fixture.Store.Dispose();
        for (var cold = NoColdRows; cold < ColdWaves; cold++)
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var database = fixture.CreateConfiguredDatabase(store);
                    await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
                    await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                    await SqlTypedInsertAssertions.UnchangedAsync(store, image, cut);
                    await RetainedAsync(fixture.Partition, store, database, state);
                    await SqlTypedInsertAssertions.ImageAsync(fixture, store, database, repaired: true, coldRows: cold);
                    await SqlTypedInsertAssertions.UnchangedAsync(store, image, cut);
                    var healthy = SqlTypedInsertHot.Execute(database, store, fixture.Partition, cold == NoColdRows ? FirstSql : SecondSql, token);
                    await SqlTypedInsertAssertions.ReceiptAsync(healthy,
                        new MutationReceipt(PutKind, SqlTypedInsertLiteral.Table, cold == NoColdRows ? FirstId : SecondId, InitialRevision));
                    await SqlTypedInsertAssertions.ImageAsync(fixture, store, database, repaired: true, coldRows: cold + NextCold);
                    image = QueueWholeFlowStorage.Bytes(store);
                    cut = store.Position;
                    await SqlTypedInsertAssertions.SameAsync(healthy.Result, SqlTypedInsertLiteral.Apply(database, healthy.Command, healthy.At));
                    await RetainedAsync(fixture.Partition, store, database, state);
                    await SqlTypedInsertAssertions.UnchangedAsync(store, image, cut);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }
    }
    private static async Task RetainedAsync(PartitionRef partition, ZoneTreeStore store, DatabaseEngine database, SqlTypedInsertState state)
    {
        var fenced = Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, SqlTypedInsertLiteral.EpochChanged));
        await SqlTypedInsertAssertions.SameAsync(fenced, SqlTypedInsertLiteral.Apply(database, state.Created.Command, state.Created.At));
        await SqlTypedInsertAssertions.SameAsync(fenced, SqlTypedInsertLiteral.Apply(database, state.Failed.Command, state.Failed.At));
        await SqlTypedInsertAssertions.SameAsync(state.Created.Bytes,
            SqlTypedInsertAssertions.OutcomeBytes(store, partition, state.Created.Command.CommandId));
        await SqlTypedInsertAssertions.SameAsync(state.Failed.Bytes,
            SqlTypedInsertAssertions.OutcomeBytes(store, partition, state.Failed.Command.CommandId));
        var repaired = state.Repaired ?? throw new InvalidOperationException(MissingRepair);
        await SqlTypedInsertAssertions.SameAsync(repaired.Result, SqlTypedInsertLiteral.Apply(database, repaired.Command, repaired.At));
        await SqlTypedInsertAssertions.SameAsync(repaired.Bytes,
            SqlTypedInsertAssertions.OutcomeBytes(store, partition, repaired.Command.CommandId));
    }
    internal const string FirstId = "zz-cold-1";
    internal const string SecondId = "zz-cold-2";
    internal const string FirstJson = "{\"id\":\"zz-cold-1\",\"title\":\"cold-one\"}";
    internal const string SecondJson = "{\"id\":\"zz-cold-2\",\"title\":\"cold-two\"}";
    private const string FirstSql = "INSERT INTO [sql-values] (id,title) VALUES (N'zz-cold-1',N'cold-one')";
    private const string SecondSql = "INSERT INTO [sql-values] (id,title) VALUES (N'zz-cold-2',N'cold-two')";
    private const string PutKind = "putDocument";
    private const string MissingRepair = "The healthy INSERT was not retained.";
    private const int NoColdRows = 0;
    private const int ColdWaves = 2;
    private const int NextCold = 1;
    private const long InitialRevision = 1;
}

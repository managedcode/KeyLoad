using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;
namespace KeyLoad.UnitTests.RelationalStorage;

internal static class SqlCreateTableCold
{
    private const int Waves = 2;
    private const long WarmRevision = 1;
    private const string Eight = "8";
    private const string Nine = "9";
    private const string EightSelected = "{\"title\":\"execution\",\"amount\":8,\"active\":true}";
    private const string NineSelected = "{\"title\":\"execution\",\"amount\":9,\"active\":true}";
    internal static async Task RunAsync(TestDatabase fixture, SqlCreateTableState state)
    {
        var identity = fixture.Store.Identity;
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        var selected = SqlCreateTableLiteral.Selected;
        var revision = WarmRevision;
        fixture.Store.Dispose();
        for (var ordinal = 0; ordinal < Waves; ordinal++)
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
                    await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                    await SqlCreateTableAssertions.UnchangedAsync(store, image, cut);
                    var database = fixture.CreateConfiguredDatabase(store);
                    await SqlCreateTableOutcomeBytes.RequireAsync(store, state);
                    await SqlCreateTablePolicy.ReplayAsync(database, state);
                    await SqlCreateTableAssertions.RowsAsync(fixture, store, database, selected, revision);
                    await SqlCreateTableAssertions.UnchangedAsync(store, image, cut);
                    var id = Guid.NewGuid();
                    var command = new CommandRequest(id, fixture.Partition,
                        [new PatchDocument(SqlCreateTableLiteral.Table, SqlCreateTableLiteral.One,
                            [new("/amount", PatchKind.Set, ordinal == 0 ? Eight : Nine)], revision)]);
                    var issued = database.EvaluationClock.GetUtcNow();
                    var receipt = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, id, issued).Get<CommitReceipt>();
                    revision++;
                    await Assert.That(receipt.CommandId).IsEqualTo(id);
                    await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
                    await SqlCreateTableAssertions.SameAsync(new MutationReceipt[]
                        { new("patchDocument", SqlCreateTableLiteral.Table, SqlCreateTableLiteral.One, revision) }, receipt.Mutations);
                    selected = ordinal == 0 ? EightSelected : NineSelected;
                    await SqlCreateTableAssertions.RowsAsync(fixture, store, database, selected, revision);
                    image = QueueWholeFlowStorage.Bytes(store);
                    cut = store.Position;
                    await SqlCreateTableAssertions.SameAsync(receipt,
                        QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, id, issued).Get<CommitReceipt>());
                    await SqlCreateTableAssertions.UnchangedAsync(store, image, cut);
                    await SqlCreateTableOutcomeBytes.RequireAsync(store, state);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}

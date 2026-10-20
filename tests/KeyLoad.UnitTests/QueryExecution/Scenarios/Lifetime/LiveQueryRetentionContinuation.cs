using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class LiveQueryRetentionContinuation
{
    private const string Root = "root";
    private const string Collection = "orders";
    private const string Id = "a";
    private const string Body = "{\"number\":7,\"status\":\"open\"}";
    private const long RetainedHorizon = 1;
    private const long UpsertSequence = 2;
    private const long UpsertRevision = 2;
    private const long DeleteSequence = 3;
    private const long DeleteRevision = 3;

    internal static async Task RunAsync(TestDatabase db, QueryEngine engine, AstQueryRequest query, string expired)
    {
        var fresh = engine.StartLiveQuery(Root, new(query));
        await Assert.That(fresh.Rows).IsEmpty();
        await Assert.That(fresh.ThroughSequence).IsEqualTo(RetainedHorizon);
        await Assert.That(fresh.CutPosition).IsEqualTo(db.Store.Position);
        var id = Guid.NewGuid();
        var issued = db.Database.EvaluationClock.GetUtcNow();
        var command = new CommandRequest(id, db.Partition,
            [new PutDocument(Collection, Id, Body, RetainedHorizon, ExplicitReplacement: true)]);
        var receipt = db.Submit(OperationKind.Batch, command, id: id, time: issued).Get<CommitReceipt>();
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var position = db.Store.Position;
        var replay = db.Submit(OperationKind.Batch, command, id: id, time: issued).Get<CommitReceipt>();
        await SameAsync(receipt, replay);
        await UnchangedAsync(db.Store, image, position);
        var request = new ReadLiveQueryRequest(query, fresh.Cursor);
        var page = engine.ReadLiveQuery(Root, request);
        await UpsertAsync(db, db.Store, page, receipt);
        await UpsertAsync(db, db.Store, engine.ReadLiveQuery(Root, request), receipt);
        await RefusedAsync(engine, query, expired);
        await UnchangedAsync(db.Store, image, position);
        var nodeId = db.Store.Identity.NodeId;
        var incarnation = db.Store.Identity.Incarnation;
        db.Store.Dispose();
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(db.Directory), db.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(cold.Identity.NodeId).IsEqualTo(nodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(incarnation);
                await UnchangedAsync(cold, image, position);
                await ColdAsync(db, cold, query, expired, command, issued, receipt, request, page, image, position);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(TestDatabase db, ZoneTreeStore cold, AstQueryRequest query,
        string expired, CommandRequest command, DateTimeOffset issued, CommitReceipt receipt,
        ReadLiveQueryRequest request, LiveQueryPage page, string[] image, long position)
    {
        var database = db.CreateConfiguredDatabase(cold);
        var coldEngine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await SameAsync(receipt, QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, command.CommandId, issued).Get<CommitReceipt>());
        await RefusedAsync(coldEngine, query, expired);
        var resumed = coldEngine.ReadLiveQuery(Root, request);
        await UpsertAsync(db, cold, resumed, receipt);
        await SameAsync(page.Changes, resumed.Changes);
        await UnchangedAsync(cold, image, position);
        await DeleteAsync(db, database, cold, coldEngine, query, resumed.Cursor);
    }

    private static async Task UpsertAsync(TestDatabase db, ZoneTreeStore store, LiveQueryPage page, CommitReceipt receipt)
    {
        var expected = new LiveQueryChange(UpsertSequence, receipt.Token, LiveQueryChangeKind.Upsert,
            db.ExpectedReference(new(db.Partition, Collection, Id)), UpsertRevision, new(Id, UpsertRevision, Body, false, []));
        await SameAsync(new[] { expected }, page.Changes);
        await Assert.That(page.ThroughSequence).IsEqualTo(UpsertSequence);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(store.Position);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    private static async Task DeleteAsync(TestDatabase db, global::KeyLoad.Core.DatabaseEngine database,
        ZoneTreeStore store, QueryEngine engine, AstQueryRequest query, string cursor)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition, [new DeleteDocument(Collection, Id, UpsertRevision)]);
        var receipt = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, id, db.Database.EvaluationClock.GetUtcNow()).Get<CommitReceipt>();
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var page = engine.ReadLiveQuery(Root, new(query, cursor));
        var expected = new LiveQueryChange(DeleteSequence, receipt.Token, LiveQueryChangeKind.Remove,
            db.ExpectedReference(new(db.Partition, Collection, Id)), DeleteRevision, null);
        await SameAsync(new[] { expected }, page.Changes);
        await Assert.That(page.ThroughSequence).IsEqualTo(DeleteSequence);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(position);
        await Assert.That(engine.ExecuteAst(Root, query).Rows).IsEmpty();
        var terminal = engine.ReadLiveQuery(Root, new(query, page.Cursor));
        await Assert.That(terminal.Changes).IsEmpty();
        await Assert.That(terminal.ThroughSequence).IsEqualTo(DeleteSequence);
        await Assert.That(terminal.HasMore).IsFalse();
        await Assert.That(terminal.CutPosition).IsEqualTo(position);
        await UnchangedAsync(store, image, position);
    }

    private static async Task RefusedAsync(QueryEngine engine, AstQueryRequest query, string cursor)
        => await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery(Root, new(query, cursor))).Code)
            .IsEqualTo(ErrorCode.HistoryUnavailable);

    private static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    private static async Task SameAsync<TExpected, TActual>(TExpected expected, TActual actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}

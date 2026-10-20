using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class LivePolicyEpochContinuation
{
    private const string Reader = "reader";
    private const string Collection = "orders";
    private const string Id = "a";
    private const string Owner = "alice";
    private const string OpenBody = "{\"status\":\"open\"}";
    private const string ClosedBody = "{\"status\":\"closed\"}";
    private const long RevokedEpoch = 3;
    private const long RepairedEpoch = 4;
    private const long OriginalRevision = 2;
    private const long VisibleRevision = 3;
    private const long RemovedRevision = 4;

    internal static async Task RunAsync(TestDatabase db, AstQueryRequest query, string original,
        string previous, PrincipalRecord principal)
    {
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        await PersistAsync(db, principal with
        {
            PolicyEpoch = RevokedEpoch,
            Revoked = true
        });
        await RefusedAsync(db.Store, engine, query, previous, ErrorCode.Unauthenticated);
        await RefusedAsync(db.Store, engine, query, null, ErrorCode.Unauthenticated);
        var repairId = Guid.NewGuid();
        var repairTime = db.Database.EvaluationClock.GetUtcNow();
        var repair = new CommandRequest(repairId, db.Partition,
            [new PutDocument(Collection, Id, OpenBody, OriginalRevision, new(Owner), true)]);
        var repaired = db.Submit(OperationKind.Batch, repair, id: repairId, time: repairTime).Get<CommitReceipt>();
        await ReceiptAsync(repaired, repairId, VisibleRevision);
        await PersistAsync(db, principal with
        {
            PolicyEpoch = RepairedEpoch
        });
        await RefusedAsync(db.Store, engine, query, original, ErrorCode.TokenInvalidated);
        await RefusedAsync(db.Store, engine, query, previous, ErrorCode.TokenInvalidated);
        var snapshotImage = QueueWholeFlowStorage.Bytes(db.Store);
        var snapshotCut = db.Store.Position;
        var fresh = engine.StartLiveQuery(Reader, new(query));
        var row = new QueryRow(Id, VisibleRevision, OpenBody, false, []);
        await SameAsync(new[] { row }, fresh.Rows);
        await Assert.That(fresh.ThroughSequence).IsEqualTo(VisibleRevision);
        await Assert.That(fresh.CutPosition).IsEqualTo(db.Store.Position);
        await SameAsync(new[] { row }, engine.ExecuteAst(Reader, query).Rows);
        await UnchangedAsync(db.Store, snapshotImage, snapshotCut);
        var id = Guid.NewGuid();
        var issued = db.Database.EvaluationClock.GetUtcNow();
        var command = new CommandRequest(id, db.Partition,
            [new PutDocument(Collection, Id, ClosedBody, VisibleRevision, new(Owner), true)]);
        var receipt = db.Submit(OperationKind.Batch, command, id: id, time: issued).Get<CommitReceipt>();
        await ReceiptAsync(receipt, id, RemovedRevision);
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var cut = db.Store.Position;
        var request = new ReadLiveQueryRequest(query, fresh.Cursor);
        var page = engine.ReadLiveQuery(Reader, request);
        await RemovedAsync(db, db.Store, page, receipt);
        await TerminalAsync(db.Store, engine, query, page.Cursor);
        await SameAsync(repaired, db.Submit(OperationKind.Batch, repair, id: repairId, time: repairTime).Get<CommitReceipt>());
        await SameAsync(receipt, db.Submit(OperationKind.Batch, command, id: id, time: issued).Get<CommitReceipt>());
        await UnchangedAsync(db.Store, image, cut);
        await ColdAsync(db, query, previous, request, receipt, command, issued, repaired, repair, repairTime, image, cut);
    }

    private static async Task ColdAsync(TestDatabase db, AstQueryRequest query, string previous,
        ReadLiveQueryRequest request, CommitReceipt receipt, CommandRequest command, DateTimeOffset issued,
        CommitReceipt repaired, CommandRequest repair, DateTimeOffset repairTime, string[] image, long cut)
    {
        var identity = db.Store.Identity;
        db.Store.Dispose();
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(db.Directory), db.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await UnchangedAsync(cold, image, cut);
                var database = db.CreateConfiguredDatabase(cold);
                var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
                await RefusedAsync(cold, engine, query, previous, ErrorCode.TokenInvalidated);
                var page = engine.ReadLiveQuery(Reader, request);
                await RemovedAsync(db, cold, page, receipt);
                await TerminalAsync(cold, engine, query, page.Cursor);
                await SameAsync(repaired, QueueWholeFlowStorage.Apply(database, OperationKind.Batch, repair, repair.CommandId, repairTime).Get<CommitReceipt>());
                await SameAsync(receipt, QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, command.CommandId, issued).Get<CommitReceipt>());
                var fresh = engine.StartLiveQuery(Reader, new(query));
                await Assert.That(fresh.Rows).IsEmpty();
                await Assert.That(fresh.ThroughSequence).IsEqualTo(RemovedRevision);
                await Assert.That(fresh.CutPosition).IsEqualTo(cut);
                await UnchangedAsync(cold, image, cut);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefusedAsync(ZoneTreeStore store, QueryEngine engine,
        AstQueryRequest query, string? cursor, ErrorCode code)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        object? partial = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = cursor is null
            ? engine.StartLiveQuery(Reader, new(query)) : engine.ReadLiveQuery(Reader, new(query, cursor)));
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
        await Assert.That(failure.Message).DoesNotContain(OpenBody);
        await Assert.That(failure.Message).DoesNotContain(Owner);
        await UnchangedAsync(store, image, cut);
    }

    private static async Task RemovedAsync(TestDatabase db, ZoneTreeStore store, LiveQueryPage page, CommitReceipt receipt)
    {
        var expected = new LiveQueryChange(RemovedRevision, receipt.Token, LiveQueryChangeKind.Remove,
            db.ExpectedReference(new(db.Partition, Collection, Id)), RemovedRevision, null);
        await SameAsync(new[] { expected }, page.Changes);
        await Assert.That(page.ThroughSequence).IsEqualTo(RemovedRevision);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(store.Position);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    private static async Task TerminalAsync(ZoneTreeStore store, QueryEngine engine, AstQueryRequest query, string cursor)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        var page = engine.ReadLiveQuery(Reader, new(query, cursor));
        await Assert.That(page.Changes).IsEmpty();
        await Assert.That(page.ThroughSequence).IsEqualTo(RemovedRevision);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(engine.ExecuteAst(Reader, query).Rows).IsEmpty();
        await UnchangedAsync(store, image, cut);
    }

    private static async Task PersistAsync(TestDatabase db, PrincipalRecord principal)
        => await SameAsync(principal, db.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());

    private static async Task ReceiptAsync(CommitReceipt receipt, Guid id, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await SameAsync(new[] { new MutationReceipt("putDocument", Collection, Id, revision) }, receipt.Mutations);
    }

    private static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long cut)
    {
        await Assert.That(store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    private static async Task SameAsync<TExpected, TActual>(TExpected expected, TActual actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}

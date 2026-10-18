using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class RelationalUniqueReplayContinuation
{
    private const string Root = "root";
    private const string Third = "third";
    private const string TwoJson = "2";
    private const string ThreeJson = "3";
    private const string AlphaSql = "SELECT * FROM \"typed-rows\" WHERE name = 'alpha'";
    private const string GammaSql = "SELECT * FROM \"typed-rows\" WHERE name = 'gamma'";
    private const string FullSql = "SELECT * FROM \"typed-rows\" ORDER BY id";
    private const string IndexPath = "index:by-name";
    private const string FullPath = "bounded-full-scan";
    private const string InitialSecond = "{\"key\":\"second\",\"name\":\"alpha\",\"count\":1,\"amount\":1.25,\"active\":true,\"at\":\"2026-10-02T12:00:00Z\"}";
    private const string ConflictingThird = "{\"key\":\"third\",\"name\":\"alpha\",\"count\":1,\"amount\":1.25,\"active\":true,\"at\":\"2026-10-02T12:00:00Z\"}";
    private const string HealthyThird = "{\"key\":\"third\",\"name\":\"gamma\",\"count\":1,\"amount\":1.25,\"active\":true,\"at\":\"2026-10-02T12:00:00Z\"}";
    private const string PatchedSecond = "{\"active\":true,\"amount\":1.25,\"at\":\"2026-10-02T12:00:00Z\",\"count\":2,\"key\":\"second\",\"name\":\"alpha\"}";
    private const string ColdSecond = "{\"active\":true,\"amount\":1.25,\"at\":\"2026-10-02T12:00:00Z\",\"count\":3,\"key\":\"second\",\"name\":\"alpha\"}";
    private const long InitialRevision = 2;
    private const long HealthyRevision = 3;
    private const long ColdRevision = 4;
    private const long ThirdRevision = 1;
    private const long HealthyTail = 6;
    private const long ColdTail = 7;

    internal static async Task RunAsync(TestDatabase db)
    {
        var before = db.Database.GetOutboxStatus(Root, db.Partition);
        await RowsAsync(db, db.Store, db.Database, InitialSecond, InitialRevision, false);
        var id = Guid.NewGuid();
        var issued = db.Database.EvaluationClock.GetUtcNow();
        var failed = Request(db, id, ConflictingThird);
        var failure = db.Submit(OperationKind.Batch, failed, id: id, time: issued);
        await Assert.That(Errors.Code(failure.Problem)).IsEqualTo(ErrorCode.Conflict);
        
        await Assert.That(failure.Value).IsNull();
        await RowsAsync(db, db.Store, db.Database, InitialSecond, InitialRevision, false);
        await SameAsync(before, db.Database.GetOutboxStatus(Root, db.Partition));
        var rejectedImage = QueueWholeFlowStorage.Bytes(db.Store);
        var rejectedCut = db.Store.Position;
        await SameAsync(failure, db.Submit(OperationKind.Batch, failed, id: id, time: issued));
        await UnchangedAsync(db.Store, rejectedImage, rejectedCut);
        var healthyId = Guid.NewGuid();
        var healthyTime = db.Database.EvaluationClock.GetUtcNow();
        var healthy = Request(db, healthyId, HealthyThird);
        var receipt = db.Submit(OperationKind.Batch, healthy, id: healthyId, time: healthyTime).Get<CommitReceipt>();
        await ReceiptAsync(receipt, healthyId,
            [new("patchDocument", RelationalTestData.Table, RelationalTestData.Second, HealthyRevision),
             new("putDocument", RelationalTestData.Table, Third, ThirdRevision)]);
        await RowsAsync(db, db.Store, db.Database, PatchedSecond, HealthyRevision, true);
        await Assert.That(db.Database.GetOutboxStatus(Root, db.Partition).Head.Tail).IsEqualTo(HealthyTail);
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var cut = db.Store.Position;
        await SameAsync(failure, db.Submit(OperationKind.Batch, failed, id: id, time: issued));
        await SameAsync(receipt, db.Submit(OperationKind.Batch, healthy, id: healthyId, time: healthyTime).Get<CommitReceipt>());
        await UnchangedAsync(db.Store, image, cut);
        await ColdAsync(db, failed, issued, failure, healthy, healthyTime, receipt, image, cut);
    }

    private static CommandRequest Request(TestDatabase db, Guid id, string json) => new(id, db.Partition,
        [new PatchDocument(RelationalTestData.Table, RelationalTestData.Second,
            [new(RelationalTestData.CountPath, PatchKind.Set, TwoJson)], InitialRevision),
         new PutDocument(RelationalTestData.Table, Third, json, 0)]);

    private static async Task ColdAsync(TestDatabase db, CommandRequest failed, DateTimeOffset issued,
        Result<object> failure, CommandRequest healthy, DateTimeOffset healthyTime, CommitReceipt receipt,
        string[] image, long cut)
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
                await SameAsync(failure, QueueWholeFlowStorage.Apply(database, OperationKind.Batch, failed, failed.CommandId, issued));
                await SameAsync(receipt, QueueWholeFlowStorage.Apply(database, OperationKind.Batch, healthy, healthy.CommandId, healthyTime).Get<CommitReceipt>());
                await RowsAsync(db, cold, database, PatchedSecond, HealthyRevision, true);
                await UnchangedAsync(cold, image, cut);
                await ColdWriteAsync(db, cold, database);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdWriteAsync(TestDatabase db, ZoneTreeStore store, DatabaseEngine database)
    {
        var id = Guid.NewGuid();
        var issued = db.Database.EvaluationClock.GetUtcNow();
        var command = new CommandRequest(id, db.Partition,
            [new PatchDocument(RelationalTestData.Table, RelationalTestData.Second,
                [new(RelationalTestData.CountPath, PatchKind.Set, ThreeJson)], HealthyRevision)]);
        var receipt = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, id, issued).Get<CommitReceipt>();
        await ReceiptAsync(receipt, id, [new("patchDocument", RelationalTestData.Table, RelationalTestData.Second, ColdRevision)]);
        await RowsAsync(db, store, database, ColdSecond, ColdRevision, true);
        await Assert.That(database.GetOutboxStatus(Root, db.Partition).Head.Tail).IsEqualTo(ColdTail);
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        await SameAsync(receipt, QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, id, issued).Get<CommitReceipt>());
        await UnchangedAsync(store, image, cut);
    }

    private static async Task RowsAsync(TestDatabase db, ZoneTreeStore store, DatabaseEngine database,
        string secondJson, long revision, bool third)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var second = new QueryRow(RelationalTestData.Second, revision, secondJson, false, []);
        var extra = new QueryRow(Third, ThirdRevision, HealthyThird, false, []);
        await PageAsync(engine.Execute(Root, new(db.Partition, AlphaSql)), [second], cut, IndexPath);
        await PageAsync(engine.Execute(Root, new(db.Partition, GammaSql)), third ? [extra] : [], cut, IndexPath);
        await PageAsync(engine.Execute(Root, new(db.Partition, FullSql, AllowFullScan: true)),
            third ? [second, extra] : [second], cut, FullPath);
        await Assert.That(database.GetDocument(Root, new(db.Partition, RelationalTestData.Table, RelationalTestData.First))).IsNull();
        if (!third)
        {
            await Assert.That(database.GetDocument(Root, new(db.Partition, RelationalTestData.Table, Third))).IsNull();
        }
        await UnchangedAsync(store, image, cut);
    }

    private static async Task PageAsync(QueryPage page, QueryRow[] rows, long cut, string path)
    {
        await SameAsync(rows, page.Rows);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(path);
    }

    private static async Task ReceiptAsync(CommitReceipt receipt, Guid id, MutationReceipt[] mutations)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await SameAsync(mutations, receipt.Mutations);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
    }

    private static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long cut)
    {
        await Assert.That(store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    private static async Task SameAsync<TExpected, TActual>(TExpected expected, TActual actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}

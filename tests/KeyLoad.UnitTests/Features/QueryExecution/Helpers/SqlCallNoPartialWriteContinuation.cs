using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlCallNoPartialWriteContinuation
{
    internal const string FirstId = "sql-call-first";
    internal const string SecondId = "sql-call-second";
    internal const string HealthyId = "sql-call-healthy";
    internal const string FirstBody = "{\"é.漢\":\"🙂 quoted \\\"value\\\"\",\"number\":1}";
    internal const string SecondBody = "{\"value\":\"/* not SQL */ ; CALL remains content\"}";
    internal const string HealthyBody = "{\"number\":2}";
    private const string PlainCall = "CALL keyload_documents_commit(@arguments)";
    private const string CommentedCall = "/* é 漢 🙂 */ CALL keyload_documents_commit(@arguments); -- end";
    private const string MultipleCall = PlainCall + "; CALL keyload_documents_commit(@arguments)";
    private const string UnsupportedCall = "CALL keyload_missing(@arguments)";
    private const string UnknownKey = "extra";
    private const string RequestKey = "request";
    private const long AbsentRevision = 0;

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var db = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                db.Configure(SqlBetweenTestData.Collection, ResourceKind.Collection);
                var id = Guid.NewGuid();
                var original = new CommandRequest(id, db.Partition, [
                    new PutDocument(SqlBetweenTestData.Collection, FirstId, FirstBody, AbsentRevision),
                    new PutDocument(SqlBetweenTestData.Collection, SecondId, SecondBody, AbsentRevision)]);
                var request = Request(original);
                await RefusedAsync(db, request with { Sql = MultipleCall }, ErrorCode.Validation);
                await RefusedAsync(db, request with { Sql = UnsupportedCall }, ErrorCode.UnsupportedCapability);
                var extra = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [RequestKey] = JsonSerializer.SerializeToElement(original, JsonDefaults.Options),
                    [UnknownKey] = JsonSerializer.SerializeToElement(SecondBody)
                };
                await RefusedAsync(db, SqlOperationTestData.Call(McpToolNames.DocumentsCommit, extra), ErrorCode.Validation);
                var compiled = SqlOperationTestData.Compile(request);
                await Assert.That(compiled.CommandId).IsEqualTo(id);
                await Assert.That(compiled.CommandKind).IsEqualTo(OperationKind.Batch);
                await Assert.That(compiled.ReadKind).IsNull();
                var decoded = Decode(compiled);
                await SqlCallNoPartialWriteAssertions.SameAsync(original, decoded);
                var issued = db.Database.EvaluationClock.GetUtcNow();
                var receipt = db.Submit(OperationKind.Batch, decoded, id: id, time: issued).Get<CommitReceipt>();
                await SqlCallNoPartialWriteAssertions.OriginalAsync(db.Database, db.Partition, receipt, id);
                await ReplayAsync(db.Database, db.Store, request, receipt, issued);
                await ColdAsync(db, request, receipt, issued);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static SqlOperationRequest Request(CommandRequest command)
        => SqlOperationTestData.Call(McpToolNames.DocumentsCommit, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [RequestKey] = JsonSerializer.SerializeToElement(command, JsonDefaults.Options) }) with
        { Sql = CommentedCall };

    private static CommandRequest Decode(McpDecodedOperation operation)
        => NativeSerialization.Deserialize<CommandRequest>(operation.Payload.Span,
            global::KeyLoad.Features.InternalSerialization.NativeValidationProfile.PublicInputElements);

    private static async Task RefusedAsync(TestDatabase db, SqlOperationRequest request, ErrorCode code)
    {
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var cut = db.Store.Position;
        McpDecodedOperation? partial = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => partial = SqlOperationTestData.Compile(request));
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
        await Assert.That(error.Message).DoesNotContain(FirstBody);
        await Assert.That(error.Message).DoesNotContain(SecondBody);
        await SqlCallNoPartialWriteAssertions.UnchangedAsync(db.Store, image, cut);
        await Assert.That(db.Database.GetDocument(SqlBetweenTestData.Root, new(db.Partition, SqlBetweenTestData.Collection, FirstId))).IsNull();
        await Assert.That(db.Database.GetDocument(SqlBetweenTestData.Root, new(db.Partition, SqlBetweenTestData.Collection, SecondId))).IsNull();
    }

    private static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, SqlOperationRequest request,
        CommitReceipt receipt, DateTimeOffset issued)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        var original = Decode(SqlOperationTestData.Compile(request));
        await SqlCallNoPartialWriteAssertions.ReceiptAsync(receipt, QueueWholeFlowStorage.Apply(database,
            OperationKind.Batch, original, original.CommandId, issued).Get<CommitReceipt>());
        await SqlCallNoPartialWriteAssertions.UnchangedAsync(store, image, cut);
        var changed = original with { Mutations = [new PutDocument(SqlBetweenTestData.Collection, FirstId, HealthyBody)] };
        var conflict = QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
            Decode(SqlOperationTestData.Compile(Request(changed))), original.CommandId, issued);
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(conflict.Json).IsNull();
        await SqlCallNoPartialWriteAssertions.UnchangedAsync(store, image, cut);
        await SqlCallNoPartialWriteAssertions.OriginalAsync(database, original.Partition, receipt, original.CommandId);
    }

    private static async Task ColdAsync(TestDatabase db, SqlOperationRequest request, CommitReceipt receipt, DateTimeOffset issued)
    {
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var cut = db.Store.Position;
        var identity = db.Store.Identity;
        db.Store.Dispose();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(db.Directory), db.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await SqlCallNoPartialWriteAssertions.UnchangedAsync(cold, image, cut);
                var database = db.CreateConfiguredDatabase(cold);
                await ReplayAsync(database, cold, request, receipt, issued);
                var id = Guid.NewGuid();
                var fresh = new CommandRequest(id, db.Partition, [new PutDocument(SqlBetweenTestData.Collection, HealthyId, HealthyBody, AbsentRevision)]);
                var time = database.EvaluationClock.GetUtcNow();
                var healthy = QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
                    Decode(SqlOperationTestData.Compile(Request(fresh))), id, time).Get<CommitReceipt>();
                await SqlCallNoPartialWriteAssertions.HealthyAsync(database, db.Partition, healthy, id);
                var after = QueueWholeFlowStorage.Bytes(cold);
                var afterCut = cold.Position;
                await SqlCallNoPartialWriteAssertions.ReceiptAsync(healthy, QueueWholeFlowStorage.Apply(database,
                    OperationKind.Batch, Decode(SqlOperationTestData.Compile(Request(fresh))), id, time).Get<CommitReceipt>());
                await SqlCallNoPartialWriteAssertions.UnchangedAsync(cold, after, afterCut);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

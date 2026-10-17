using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class QueueGraphIdentityFlow
{
    private const string CanonicalVertexMismatch = "A canonical graph vertex record is inconsistent.";

    internal static async Task RunAsync(TestDatabase db, bool from, int component)
    {
        var state = await SeedAsync(db, from);
        var reference = from ? state.First.Reference : state.Second.Reference;
        var foreign = new PartitionRef(db.Partition.TenantId, db.Partition.DatabaseId,
            db.Partition.TransactionDomainId, QueueGraphIdentityState.ForeignPartition);
        var mismatched = component switch
        {
            0 => reference with { Partition = foreign },
            1 => reference with { Collection = "other" },
            2 => reference with { Id = "other" },
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
        var record = from ? state.First : state.Second;
        var corrupted = NativeSerialization.Serialize(record with
        {
            Reference = mismatched
        });
        await RunFaultAsync(state with
        {
            FailureDetail = CanonicalVertexMismatch
        }, corrupted);
    }

    internal static async Task RunReadyAsync(TestDatabase db)
    {
        var state = await SeedAsync(db, from: true);
        var key = KeySpace.Partition("message-meta", db.Partition,
            QueueGraphIdentityState.Queue, QueueGraphIdentityState.Message);
        var original = db.Store.Read(view => view.ReadOwnedValue(key))!;
        var metadata = NativeSerialization.Deserialize<MessageMetadata>(original);
        var changed = metadata with
        {
            ReadySequence = metadata.ReadySequence + 1
        };
        state = state with
        {
            EndpointKey = key,
            OriginalEndpoint = original,
            FaultSource = state.Source with
            {
                Metadata = changed
            },
            FailureCode = ErrorCode.RecoveryRequired,
            FailureDetail = "The recorded queue retry decision no longer matches its input."
        };
        await RunFaultAsync(state, NativeSerialization.Serialize(changed));
    }

    internal static async Task RunFaultAsync(QueueGraphIdentityState state, byte[] corrupted)
    {
        var db = state.Fixture;
        db.Store.Commit((tx, _) => { tx.Put(state.EndpointKey, corrupted); return true; });
        await QueueGraphIdentityAssertions.RefusesAsync(state, db.Database, db.Store);
        await QueueGraphIdentityAssertions.RefusesAsync(state, db.Database, db.Store);
        state.ColdImage = global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(db.Store);
        state.ColdPosition = db.Store.Position;
        db.Store.Dispose();
        await QueueGraphIdentityCold.RunAsync(state, dirty: true);
        await QueueGraphIdentityCold.RunAsync(state, dirty: true, repair: true);
        await QueueGraphIdentityCold.RunAsync(state, dirty: false);
        await QueueGraphIdentityCold.RunAsync(state, dirty: false);
    }

    internal static async Task<QueueGraphIdentityState> SeedAsync(TestDatabase db, bool from)
    {
        var first = new EntityRef(db.Partition, QueueGraphIdentityState.Collection, "first");
        var second = new EntityRef(db.Partition, QueueGraphIdentityState.Collection, "second");
        var boundFrom = DatabaseCompositionExpectedReferences.Capture(db, first);
        var boundTo = DatabaseCompositionExpectedReferences.Capture(db, second);
        var marker = DatabaseCompositionExpectedReferences.Capture(db,
            new(db.Partition, QueueGraphIdentityState.Collection, QueueGraphIdentityState.Marker));
        var at = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var link = new QueueGraphLink(first, second, QueueGraphIdentityState.Label,
            QueueGraphIdentityState.AttributesJson);
        var payload = QueueGraphIdentityAssertions.Payload(link);
        var seed = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(QueueGraphIdentityState.Collection, first.Id, QueueGraphIdentityState.FirstJson),
                new PutDocument(QueueGraphIdentityState.Collection, second.Id, QueueGraphIdentityState.SecondJson),
                new EnqueueMessage(QueueGraphIdentityState.Queue, QueueGraphIdentityState.Message,
                    payload, HeadersJson: QueueGraphIdentityState.HeadersJson)]);
        var operation = await CompileAsync(db.Database, seed, at);
        var receipt = db.Database.Apply(operation).Get<CommitReceipt>();
        var expectedReceipt = new CommitReceipt(operation.Id,
            new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId,
                db.Store.Position, receipt.Token.OwnershipEpoch),
            [new("putDocument", QueueGraphIdentityState.Collection, first.Id, 1),
                new("putDocument", QueueGraphIdentityState.Collection, second.Id, 1),
                new("enqueue", QueueGraphIdentityState.Queue, QueueGraphIdentityState.Message, 1)],
            receipt.Durability);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedReceipt))).IsTrue();
        var failedRequest = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(QueueGraphIdentityState.Collection, QueueGraphIdentityState.Marker,
                QueueGraphIdentityState.MarkerJson),
                new QueueToGraph(QueueGraphIdentityState.Graph, QueueGraphIdentityState.Queue,
                    QueueGraphIdentityState.Prefix)]);
        var failed = await CompileAsync(db.Database, failedRequest, at.AddSeconds(1));
        var healthy = await CompileAsync(db.Database, failedRequest with
        {
            CommandId = Guid.NewGuid()
        }, at.AddSeconds(2));
        var reference = from ? first : second;
        var key = global::KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(reference);
        var original = db.Store.Read(view => view.ReadOwnedValue(key))!;
        var source = new MessageInspection(new(QueueGraphIdentityState.Message, MessageState.Ready,
            0, 1, 1, null, null), payload, QueueGraphIdentityState.HeadersJson);
        var state = new QueueGraphIdentityState(db, first, second, at,
            new(boundFrom, 1, QueueGraphIdentityState.FirstJson, new(), at),
            new(boundTo, 1, QueueGraphIdentityState.SecondJson, new(), at), source,
            operation, receipt, failed, healthy, key, original, boundFrom, boundTo, marker);
        await QueueGraphIdentityAssertions.ModelsAsync(state, db.Database, db.Store, healthy: false);
        return state;
    }

    internal static async Task<ReplicatedOperation> CompileAsync(DatabaseEngine engine, CommandRequest command, DateTimeOffset at)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [global::KeyLoad.Server.McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(command, JsonDefaults.Options)
        };
        var request = new SqlOperationRequest(command.Partition, "EXEC keyload_documents_commit @arguments",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [global::KeyLoad.UnitTests.Features.QueryExecution.SqlOperationTestData.Parameter] = JsonSerializer.SerializeToElement(arguments, JsonDefaults.Options)
            });
        var compiled = SqlOperationTestData.Compile(request);
        await Assert.That(compiled.CommandKind).IsEqualTo(OperationKind.Batch);
        await Assert.That(compiled.ReadKind).IsNull();
        await Assert.That(compiled.CommandId).IsEqualTo(command.CommandId);
        var decoded = NativeSerialization.Deserialize<CommandRequest>(compiled.Payload.Span);
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(command))).IsTrue();
        var work = new ReadExecutionBudget(engine.OperationLimitsOptions, engine.EvaluationClock,
            TestContext.Current!.Execution.CancellationToken);
        return engine.CreateNativeOperation(OperationKind.Batch, command.CommandId, QueueGraphIdentityState.Root, at,
            NativeSerialization.Serialize(decoded), work);
    }
}

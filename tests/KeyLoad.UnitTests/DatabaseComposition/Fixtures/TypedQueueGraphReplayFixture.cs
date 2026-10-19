using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class TypedQueueGraphReplayFixture
{
    private const string FromCollection = "typed-from";
    private const string ToCollection = "typed-to";
    private const string PrimaryKey = "id";
    private const string Name = "name";
    private const string First = "first";
    private const string Second = "second";
    private const string FirstJson = """{"id":"first","name":"first"}""";
    private const string SecondJson = """{"id":"second","name":"second"}""";
    private const int MaximumMessages = 1;
    private const int NoAttempts = 0;
    private const string InvalidFixtureResource = "The typed replay fixture resource differs from its declared schema.";
    private const long FirstRevision = 1;
    private const int ForwardOffsetSeconds = 1;
    private const int HealthyOffsetSeconds = 2;

    internal static async Task<QueueGraphIdentityState> SeedAsync(TestDatabase db, bool from)
    {
        db.Configure(QueueGraphIdentityState.Collection, ResourceKind.Collection);
        db.Configure(QueueGraphIdentityState.Graph, ResourceKind.Graph);
        db.Configure(QueueGraphIdentityState.Queue, ResourceKind.WorkQueue);
        ConfigureTyped(db, FromCollection);
        ConfigureTyped(db, ToCollection);
        var first = new EntityRef(db.Partition, FromCollection, First);
        var second = new EntityRef(db.Partition, ToCollection, Second);
        var boundFrom = DatabaseCompositionExpectedReferences.Capture(db, first);
        var boundTo = DatabaseCompositionExpectedReferences.Capture(db, second);
        var marker = DatabaseCompositionExpectedReferences.Capture(db,
            new(db.Partition, QueueGraphIdentityState.Collection, QueueGraphIdentityState.Marker));
        var at = TimeProvider.System.GetUtcNow().AddSeconds(ForwardOffsetSeconds);
        var payload = QueueGraphIdentityAssertions.Payload(new(first, second,
            QueueGraphIdentityState.Label, QueueGraphIdentityState.AttributesJson));
        var seed = await QueueGraphIdentityFlow.CompileAsync(db.Database, new(Guid.NewGuid(), db.Partition,
            [new PutDocument(FromCollection, First, FirstJson),
             new PutDocument(ToCollection, Second, SecondJson),
             new EnqueueMessage(QueueGraphIdentityState.Queue, QueueGraphIdentityState.Message,
                 payload, HeadersJson: QueueGraphIdentityState.HeadersJson)]), at);
        var receipt = db.Database.Apply(seed).Get<CommitReceipt>();
        await SeedReceiptAsync(db, seed, receipt);
        var failed = await QueueGraphIdentityFlow.CompileAsync(db.Database, new(Guid.NewGuid(), db.Partition,
            [new PutDocument(QueueGraphIdentityState.Collection, QueueGraphIdentityState.Marker,
                QueueGraphIdentityState.MarkerJson),
             new QueueToGraph(QueueGraphIdentityState.Graph, QueueGraphIdentityState.Queue,
                QueueGraphIdentityState.Prefix, MaximumMessages, FromCollection, ToCollection)]),
            at.AddSeconds(ForwardOffsetSeconds));
        var command = OrleansSerialization.Deserialize<CommandRequest>(failed.Value!.Input.Span);
        var healthy = await QueueGraphIdentityFlow.CompileAsync(
            db.Database, command with
            {
                CommandId = Guid.NewGuid()
            }, at.AddSeconds(HealthyOffsetSeconds));
        var key = global::KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(from ? first : second);
        var original = db.Store.Read(view => view.ReadOwnedValue(key))!;
        var state = new QueueGraphIdentityState(db, first, second, at,
            new(boundFrom, FirstRevision, FirstJson, new(), at), new(boundTo, FirstRevision, SecondJson, new(), at),
            new(new(QueueGraphIdentityState.Message, MessageState.Ready, NoAttempts, FirstRevision, FirstRevision, null, null),
                payload, QueueGraphIdentityState.HeadersJson), seed, receipt, failed, healthy, key, original, boundFrom, boundTo, marker);
        await QueueGraphIdentityAssertions.ModelsAsync(state, db.Database, db.Store, healthy: false);
        return state;
    }

    private static void ConfigureTyped(TestDatabase db, string name)
    {
        var resource = new ResourceDefinition(name, ResourceKind.Collection, db.Partition.TransactionDomainId)
        {
            RelationalSchema = new(PrimaryKey, [new(PrimaryKey, RelationalColumnType.Text),
            new(Name, RelationalColumnType.Text)])
        };
        var actual = db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        if (!JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(resource)))
        {
            throw new InvalidOperationException(InvalidFixtureResource);
        }
    }

    private static async Task SeedReceiptAsync(TestDatabase db, Command<DatabaseCommand> operation, CommitReceipt actual)
    {
        var expected = new CommitReceipt(operation.CommandId, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId,
                db.Store.Position), [new("putDocument", FromCollection, First, FirstRevision),
             new("putDocument", ToCollection, Second, FirstRevision),
             new("enqueue", QueueGraphIdentityState.Queue, QueueGraphIdentityState.Message, FirstRevision)], actual.Durability, (actual.Durability == DurabilityProfile.Buffered ? 0 : 1));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

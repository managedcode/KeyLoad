using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationCoordinatorTests
{
    private const string RootPrincipal = "root";
    private const string Collection = "native-operations";
    private const string Entity = "native-entry";
    private const string Json = "{\"name\":\"界λ\"}";
    private static readonly DateTimeOffset Time = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NativeCommandPersistsTypedOutcomeAndReplaysTheSameStableId()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(Collection, Entity, Json)]);
        var coordinator = new EmbeddedCoordinator(database.Database);
        var payload = NativeSerialization.Serialize(request);
        var first = await coordinator.SubmitNativeAsync(OperationKind.Batch, id, RootPrincipal, payload);
        var replay = await coordinator.SubmitNativeAsync(OperationKind.Batch, id, RootPrincipal, payload);
        await Assert.That(first.Json).IsNull();
        await Assert.That(first.NativeValue).IsTypeOf<CommitReceipt>();
        await Assert.That(replay.Get<CommitReceipt>().Token).IsEqualTo(first.Get<CommitReceipt>().Token);
        await Assert.That(replay.Get<CommitReceipt>().CommandId).IsEqualTo(id);
        await Assert.That(database.Database.GetDocument(RootPrincipal, new(database.Partition, Collection, Entity))!.Revision)
            .IsEqualTo(1L);
    }

    [Test]
    public async Task WrongNativeCommandTypeFailsBeforeAnyStoreMutation()
    {
        using var database = new TestDatabase();
        var before = database.Store.Position;
        var coordinator = new EmbeddedCoordinator(database.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => coordinator.SubmitNativeAsync(OperationKind.Batch,
            Guid.NewGuid(), RootPrincipal, NativeSerialization.Serialize(true)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    public async Task NativeOperationRetainsFrozenPublicIdentityAndOpaqueJson()
    {
        using var database = new TestDatabase();
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(Collection, Entity, Json)]);
        var operation = database.Database.CreateNativeOperation(OperationKind.Batch, id, RootPrincipal, Time,
            NativeSerialization.Serialize(request));
        await Assert.That(operation.PayloadJson).IsEqualTo(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(request)));
        await Assert.That(operation.NativePayload.IsEmpty).IsFalse();
        var wrapper = NativeSerialization.Deserialize<KeyLoad.Core.Features.InternalSerialization.NativeCommandPayload>(
            operation.NativePayload.Span);
        var restored = NativeSerialization.Deserialize<CommandRequest>(wrapper.Value.Span);
        await Assert.That(((PutDocument)restored.Mutations[0]).Json).IsEqualTo(Json);
    }
}

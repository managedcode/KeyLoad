namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class OperationCoordinatorTests
{
    private const string RootPrincipal = "root";
    private const string Collection = "execution-operations";
    private const string Entity = "execution-entry";
    private const string Json = "{\"name\":\"界λ\"}";
    private static readonly DateTimeOffset Time = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CommandPersistsTypedOutcomeAndReplaysTheSameStableId()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(Collection, Entity, Json)]);
        var coordinator = new EmbeddedCoordinator(database.Database);
        var payload = OrleansSerialization.Serialize(request);
        var first = await coordinator.RuntimeSubmitAsync(OperationKind.Batch, id, RootPrincipal, payload);
        var replay = await coordinator.RuntimeSubmitAsync(OperationKind.Batch, id, RootPrincipal, payload);
        await Assert.That(first.Value).IsTypeOf<CommitReceipt>();
        await Assert.That(replay.Get<CommitReceipt>().Token).IsEqualTo(first.Get<CommitReceipt>().Token);
        await Assert.That(replay.Get<CommitReceipt>().CommandId).IsEqualTo(id);
        await Assert.That(database.Database.GetDocument(RootPrincipal, new(database.Partition, Collection, Entity))!.Revision)
            .IsEqualTo(1L);
    }

    [Test]
    public async Task WrongCommandTypeFailsBeforeAnyStoreMutation()
    {
        using var database = new TestDatabase();
        var before = database.Store.Position;
        var coordinator = new EmbeddedCoordinator(database.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => coordinator.RuntimeSubmitAsync(OperationKind.Batch,
            Guid.NewGuid(), RootPrincipal, OrleansSerialization.Serialize(true)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    public async Task CommandRetainsExactInputAndOpaqueUserJson()
    {
        using var database = new TestDatabase();
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(Collection, Entity, Json)]);
        var operation = database.Database.CreateCommand(OperationKind.Batch, id, RootPrincipal, Time,
            OrleansSerialization.Serialize(request));
        await Assert.That(operation.CommandId).IsEqualTo(id);
        await Assert.That(operation.UserId).IsEqualTo(RootPrincipal);
        await Assert.That(operation.Timestamp).IsEqualTo(Time.UtcDateTime);
        await Assert.That(operation.Value!.Input.Span.SequenceEqual(OrleansSerialization.Serialize(request))).IsTrue();
        var restored = OrleansSerialization.Deserialize<CommandRequest>(operation.Value.Input.Span);
        await Assert.That(((PutDocument)restored.Mutations[0]).Json).IsEqualTo(Json);
    }
}

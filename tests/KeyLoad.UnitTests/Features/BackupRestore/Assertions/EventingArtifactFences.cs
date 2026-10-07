namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class EventingArtifactFences
{
    private const string InvalidSignedToken = "The signed token is invalid.";
    private const string QueuePaused = "Queue dispatch is paused.";
    private const string GroupPaused = "Subscription delivery is paused.";
    private const string AdministrationRequired = "Cluster administration is required.";
    private const string Reader = "backup-reader";

    internal static async Task VerifyAsync(EventingArtifactFixture fixture)
    {
        var database = fixture.Database;
        var before = database.Store.Position;
        var bytes = EventingArtifactState.Bytes(database.Store, fixture.Source.Partition);
        await Assert.That(database.Apply(fixture.Original).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadEventSource(
            EventingArtifactFixture.Principal, new(fixture.Events, Cursor: fixture.Cursor)));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        invalid = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadEventSource(
            EventingArtifactFixture.Principal, new(fixture.Events with { Generation = 2 })));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var id = Guid.NewGuid();
        await FailedAsync(fixture, OperationKind.Receive,
            new ReceiveRequest(id, fixture.Lane), id, ErrorCode.DispatchPaused, QueuePaused);
        id = Guid.NewGuid();
        await FailedAsync(fixture, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, fixture.Group), id, ErrorCode.DispatchPaused, GroupPaused);
        id = Guid.NewGuid();
        await FailedAsync(fixture, OperationKind.Delivery,
            new DeliveryCommand(id, fixture.Lane, fixture.QueueClaim.Deliveries[0].Token, DeliveryAction.Ack),
            id, ErrorCode.TokenInvalidated, InvalidSignedToken);
        id = Guid.NewGuid();
        await FailedAsync(fixture, OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, fixture.Group, fixture.Received.Deliveries[1].Token, DeliveryAction.Ack),
            id, ErrorCode.TokenInvalidated, InvalidSignedToken);
        await EventingArtifactState.SameAsync(bytes, database.Store, fixture.Source.Partition);
        var principal = new PrincipalRecord(Reader, fixture.Source.Partition.TenantId,
            [new(fixture.Source.Partition.DatabaseId, EventingArtifactFixture.Topic, Capability.TopicsRead)], []);
        id = Guid.NewGuid();
        fixture.Apply(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), id).Get<PrincipalRecord>();
        id = Guid.NewGuid();
        await FailedAsync(fixture, OperationKind.SetDispatch, false, id,
            ErrorCode.PermissionDenied, AdministrationRequired, Reader);
        id = Guid.NewGuid();
        await FailedAsync(fixture, OperationKind.Receive, new ReceiveRequest(id, fixture.Lane), id,
            ErrorCode.DispatchPaused, QueuePaused);
        before = database.Store.Position;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => database.GetSubscription(Reader, fixture.Group));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(database.ReadEventSource(Reader, new(fixture.Events)).Events.Length).IsEqualTo(3);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await EventingArtifactState.SameAsync(bytes, database.Store, fixture.Source.Partition);
    }

    internal static async Task FailedAsync<T>(EventingArtifactFixture fixture, OperationKind kind, T payload,
        Guid id, ErrorCode expected, string detail, string principal = EventingArtifactFixture.Principal)
    {
        var before = fixture.Database.Store.Position;
        var original = fixture.Operation(kind, payload, id, principal);
        var result = fixture.Database.Apply(original);
        await Assert.That(result.Error).IsEqualTo(expected);
        await Assert.That(result.SafeDetail).IsEqualTo(detail);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(before + 1);
        var stored = EventingArtifactState.FullBytes(fixture.Database.Store);
        await EventingArtifactState.SameResultAsync(result, fixture.Database.Apply(original));
        await Assert.That(EventingArtifactState.FullBytes(fixture.Database.Store)).IsEquivalentTo(stored, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(before + 1);
    }
}

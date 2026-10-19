using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupHeldLease
{
    internal static async Task RequireNaturalExpiryAsync(EventingArtifactFixture fixture,
        MixedEventingRetentionBackupState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var store = fixture.Target!;
        var issued = state.Queue.Successes.Single(item => item.Operation.Value!.Kind == OperationKind.Receive
            && item.Result.Get<ReceiveResult>().Deliveries.Any(delivery => delivery.Id == QueueLifecycleTestProtocol.Held));
        var original = fixture.Source.ReadStoredOutcome(issued.Operation.CommandId).PreparedCommand;
        await Assert.That(original.CommandId).IsEqualTo(issued.Operation.CommandId);
        await Assert.That(original.UserId!).IsEqualTo(QueueLifecycleTestProtocol.Administrator);
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.Receive);
        await Assert.That(original.Value.Fingerprint).IsEqualTo(issued.Operation.Value!.Fingerprint);
        _ = fixture.Source.Database.OwnCommand(original);
        await MixedEventingRetentionBackupTopic.EqualAsync(new ReceiveRequest(issued.Operation.CommandId,
                state.Queue.Lane, LeaseSeconds: QueueLifecycleTestProtocol.LeaseSeconds),
            NativeSerialization.Deserialize<ReceiveRequest>(original.Value.Input.Span));
        var deadline = new DateTimeOffset(original.Timestamp).AddSeconds(QueueLifecycleTestProtocol.LeaseSeconds);
        var held = fixture.Database.InspectMessage(QueueLifecycleTestProtocol.Administrator,
            state.Queue.Lane, QueueLifecycleTestProtocol.Held)!;
        await Assert.That(held.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(held.Metadata.StateVersion).IsEqualTo(QueueLifecycleTestProtocol.Two);
        await Assert.That(held.Metadata.DeliveryGeneration).IsEqualTo(QueueLifecycleTestProtocol.One);
        await Assert.That(held.Metadata.LeaseUntil).IsEqualTo(deadline);
        var image = QueueLifecycleImage.Capture(store, state.Queue.Lane);
        var physical = EventingArtifactState.FullBytes(store);
        var position = store.Position;
        var clock = fixture.Database.EvaluationClock;
        var remaining = deadline - clock.GetUtcNow();
        while (remaining > TimeSpan.Zero)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)), clock, token);
            remaining = deadline - clock.GetUtcNow();
        }
        token.ThrowIfCancellationRequested();
        await Assert.That(clock.GetUtcNow() >= deadline).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await QueueLifecycleImage.SameAsync(store, state.Queue.Lane, image);
        await Assert.That(EventingArtifactState.FullBytes(store))
            .IsEquivalentTo(physical, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}

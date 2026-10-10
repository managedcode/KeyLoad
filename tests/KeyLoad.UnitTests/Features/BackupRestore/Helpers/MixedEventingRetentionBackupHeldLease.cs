using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupHeldLease
{
    internal static async Task RequireNaturalExpiryAsync(EventingArtifactFixture fixture,
        MixedEventingRetentionBackupState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var store = fixture.Target!;
        var deadline = state.Queue.Time.AddSeconds(QueueLifecycleTestProtocol.LeaseSeconds);
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
        var now = clock.GetUtcNow();
        if (now < deadline)
        { await Task.Delay(deadline - now, clock, token); }
        token.ThrowIfCancellationRequested();
        await Assert.That(clock.GetUtcNow() >= deadline).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await QueueLifecycleImage.SameAsync(store, state.Queue.Lane, image);
        await Assert.That(EventingArtifactState.FullBytes(store))
            .IsEquivalentTo(physical, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}

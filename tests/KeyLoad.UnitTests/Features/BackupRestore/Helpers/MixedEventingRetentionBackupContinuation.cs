using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupContinuation
{
    internal static async Task RunAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        await EventingArtifactFences.VerifyAsync(fixture);
        var oldAck = new DeliveryCommand(Guid.NewGuid(), state.InboxSource, state.InboxDelivery.Token, DeliveryAction.Ack);
        await EventingArtifactFences.FailedAsync(fixture, OperationKind.Delivery, oldAck, oldAck.CommandId,
            ErrorCode.TokenInvalidated, "The signed token is invalid.");
        var cut = fixture.Target!.Position;
        await Assert.That(TargetInboxNativeSetup.Apply(fixture.Database, state.Inbox, token).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(fixture.Target.Position).IsEqualTo(cut);
        await Assert.That(fixture.Database.Apply(fixture.Operation(OperationKind.Batch, state.Purge,
            state.Purge.CommandId)).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await MixedEventingRetentionBackupOracle.InitialAsync(fixture, state, fixture.Database);
        await MixedEventingRetentionBackupPrivacy.RunAsync(fixture, state, token);
        await MixedEventingRetentionBackupHeldLease.RequireNaturalExpiryAsync(fixture, state, token);
        state.RestoredQueue = new(state.Queue.Partition, state.Queue.Time)
        { NativeSubmit = operation => fixture.SubmitIssued(fixture.Database, operation, explicitTime: false, token) };
        await QueueLifecycleRestoredContinuation.RunAsync(fixture.Database, fixture.Target, state.RestoredQueue, token);
        await GroupsAsync(fixture);
        var current = fixture.ApplyInbox(fixture.Database,
            state.Inbox with { CommandId = Guid.NewGuid() }, token).Get<CommitInboxResult>();
        await Assert.That(current.AlreadyProcessed).IsTrue();
        await Assert.That(current.OriginalEffectsToken).IsEqualTo(state.InboxResult.OriginalEffectsToken);
        await TargetInboxNativeAssertions.CapacityAsync(fixture.Target, state.Inbox, TargetInboxUnitProtocol.FirstRevision);
        await TargetInboxNativePermissions.ReplayAsync(fixture.Database, fixture.Target, state.Inbox, state.InboxResult, token);
        await MixedEventingRetentionBackupHealthy.RunAsync(fixture, state, token);
    }

    private static async Task GroupsAsync(EventingArtifactFixture fixture)
    {
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), fixture.Group, 1, SubscriptionStart.FromBeginning);
        var sought = fixture.Apply(fixture.Database, OperationKind.SeekSubscription, seek, seek.CommandId).Get<SubscriptionInfo>();
        await MixedEventingRetentionBackupTopic.EqualAsync(
            new SubscriptionInfo(fixture.Group, new(EventingArtifactFixture.Principal), 2, 2, 0, 0, 3, true, null), sought);
        var pause = new SetSubscriptionPausedRequest(Guid.NewGuid(), fixture.Group, sought.Generation, false);
        _ = fixture.Apply(fixture.Database, OperationKind.SetSubscriptionPaused, pause, pause.CommandId).Get<SubscriptionInfo>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), fixture.Group, MaxEvents: 3);
        var received = fixture.Apply(fixture.Database, OperationKind.ReceiveSubscription, request, request.RequestId).Get<ReceiveSubscriptionResult>();
        await MixedEventingRetentionBackupTopic.EqualAsync(fixture.Received.Deliveries.Select(item => item.Event).ToArray(),
            received.Deliveries.Select(item => item.Event).ToArray());
        var process = new SubscriptionProcessingRequest(Guid.NewGuid(), fixture.Group, received.Deliveries[0].Token,
            EventingArtifactFixture.Handler, 1, [.. EventingArtifactFixture.Effects]);
        var result = fixture.Apply(fixture.Database, OperationKind.SubscriptionProcessing, process, process.CommandId).Get<SubscriptionProcessingResult>();
        await Assert.That(result.AlreadyProcessed).IsTrue();
        await Assert.That(result.OriginalEffectsToken).IsEqualTo(fixture.Processed.OriginalEffectsToken);
        foreach (var item in received.Deliveries.Skip(1))
        {
            var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), fixture.Group, item.Token, DeliveryAction.Ack);
            _ = fixture.Apply(fixture.Database, OperationKind.SubscriptionDelivery, ack, ack.CommandId).Get<CommitReceipt>();
        }
        await Assert.That(fixture.Database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group).Checkpoint).IsEqualTo(3L);
    }
}

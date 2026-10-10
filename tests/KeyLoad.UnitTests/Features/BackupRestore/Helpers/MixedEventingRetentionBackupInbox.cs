using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupInbox
{
    private const string Fresh = "mixed-restored-input";
    private const string FreshDocument = "mixed-restored-inbox-effect";

    internal static async Task ContinueAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.InboxSource.Partition,
            [new CancelQueueMessage(state.InboxSource.Queue, state.Inbox.MessageId, 2, 1),
             new EnqueueMessage(state.InboxSource.Queue, Fresh, TargetInboxUnitProtocol.Payload)]);
        _ = fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), state.InboxSource);
        var result = fixture.Apply(fixture.Database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>();
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(Fresh);
        await Assert.That(delivery.PayloadJson).IsEqualTo(TargetInboxUnitProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(EventingArtifactFixture.EmptyJson);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(fixture.Time.AddSeconds(30));
        var request = state.Inbox with
        {
            CommandId = Guid.NewGuid(),
            MessageId = Fresh,
            Effects = [new PutDocument(TargetInboxUnitProtocol.Collection, FreshDocument, TargetInboxUnitProtocol.Payload, 0)]
        };
        var original = TargetInboxNativeSetup.Apply(fixture.Database, request, token).Get<CommitInboxResult>();
        await Assert.That(original.AlreadyProcessed).IsFalse();
        var cut = fixture.Target!.Position;
        await MixedEventingRetentionBackupTopic.EqualAsync(original,
            TargetInboxNativeSetup.Apply(fixture.Database, request, token).Get<CommitInboxResult>());
        await Assert.That(fixture.Target.Position).IsEqualTo(cut);
        await Assert.That(fixture.Database.InspectMessage(EventingArtifactFixture.Principal, state.InboxSource, Fresh)!.Metadata.State)
            .IsEqualTo(MessageState.Leased);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.InboxSource, delivery.Token, DeliveryAction.Ack);
        _ = fixture.Apply(fixture.Database, OperationKind.Delivery, ack, ack.CommandId).Get<CommitReceipt>();
        var expected = new MessageInspection(new(Fresh, MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), null, null);
        await MixedEventingRetentionBackupTopic.EqualAsync(expected,
            fixture.Database.InspectMessage(EventingArtifactFixture.Principal, state.InboxSource, Fresh));
        var document = fixture.Database.GetDocument(EventingArtifactFixture.Principal,
            new(state.Inbox.Target.Partition, TargetInboxUnitProtocol.Collection, FreshDocument), cancellationToken: token);
        await Assert.That(document!.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(TargetInboxUnitProtocol.Payload);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        await TargetInboxNativeAssertions.CapacityAsync(fixture.Target, state.Inbox, TargetInboxUnitProtocol.ReceiptCapacity);
        state.HealthyInbox = request;
        state.HealthyInboxResult = original;
    }
}

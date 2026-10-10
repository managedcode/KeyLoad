using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupInbox
{
    private const string Fresh = "mixed-restored-input";
    private const string FreshDocument = "mixed-restored-inbox-effect";
    private const string Operator = "mixed-inbox-operator";

    internal static async Task ContinueAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.InboxSource.Partition,
            [new CancelQueueMessage(state.InboxSource.Queue, state.Inbox.MessageId, 2, 1),
             new EnqueueMessage(state.InboxSource.Queue, Fresh, TargetInboxUnitProtocol.Payload)]);
        await RefusedRootAsync(fixture, command);
        var principal = new PrincipalRecord(Operator, state.InboxSource.Partition.TenantId,
            [new(state.InboxSource.Partition.DatabaseId, state.InboxSource.Queue,
                Capability.QueueCancel | Capability.QueuePublish)], [])
        { ClusterAdministrator = true };
        var admitted = fixture.Apply(fixture.Database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal), Guid.NewGuid()).Get<PrincipalRecord>();
        await MixedEventingRetentionBackupTopic.EqualAsync(principal, admitted);
        command = command with { CommandId = Guid.NewGuid() };
        _ = fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId, Operator).Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), state.InboxSource);
        var result = fixture.Apply(fixture.Database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>();
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(Fresh);
        await Assert.That(delivery.PayloadJson).IsEqualTo(TargetInboxUnitProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(EventingArtifactFixture.EmptyJson);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        var issued = fixture.ReadTargetIssuedOperation(receive.RequestId);
        await Assert.That(issued.Id).IsEqualTo(receive.RequestId);
        await Assert.That(issued.Kind).IsEqualTo(OperationKind.Receive);
        await Assert.That(issued.PrincipalId).IsEqualTo(EventingArtifactFixture.Principal);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(issued.EvaluatedAt.AddSeconds(30));
        var request = state.Inbox with
        {
            CommandId = Guid.NewGuid(),
            MessageId = Fresh,
            Effects = [new PutDocument(TargetInboxUnitProtocol.Collection, FreshDocument, TargetInboxUnitProtocol.Payload, 0)]
        };
        var original = fixture.ApplyInbox(fixture.Database, request, token).Get<CommitInboxResult>();
        await Assert.That(original.AlreadyProcessed).IsFalse();
        var cut = fixture.Target!.Position;
        await MixedEventingRetentionBackupTopic.EqualAsync(original,
            fixture.ApplyInbox(fixture.Database, request, token).Get<CommitInboxResult>());
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

    private static async Task RefusedRootAsync(EventingArtifactFixture fixture, CommandRequest command)
    {
        var before = fixture.Target!.Position;
        var result = fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(result.SafeDetail).IsEqualTo("The principal cannot perform this operation in this scope.");
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(fixture.Target.Position).IsEqualTo(before + 1);
        var original = fixture.ReadTargetIssuedOperation(command.CommandId);
        await EventingArtifactState.SameResultAsync(result, fixture.Database.ResolveOutcome(original));
        var rows = EventingArtifactState.FullBytes(fixture.Target);
        var journal = fixture.ReadTargetJournalCut();
        await EventingArtifactState.SameResultAsync(result,
            fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId));
        await Assert.That(fixture.ReadTargetJournalCut()).IsEqualTo(journal);
        await Assert.That(fixture.Target.Position).IsEqualTo(before + 1);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Target))
            .IsEquivalentTo(rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

}

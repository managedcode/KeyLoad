using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3Inbox
{
    internal static async Task SeedAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var enqueue = new CommandRequest(Guid.NewGuid(), state.Source.Partition,
            [new EnqueueMessage(state.Source.Queue, ClusterRestoreMixedRetentionRf3Protocol.OriginalInput,
                ClusterRestoreMixedRetentionRf3Protocol.EventPayload, ClusterRestoreMixedRetentionRf3Protocol.EventHeaders)]);
        _ = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Source.Partition,
            McpCallerTools.DocumentsCommit, enqueue, () => callers.Sdk.CommitAsync(enqueue, token), token);
        state.InputRequest = new(Guid.NewGuid(), state.Source);
        state.InputReceived = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Source.Partition,
            McpCallerTools.MessagesReceive, state.InputRequest, () => callers.Sdk.ReceiveAsync(state.InputRequest, token), token);
        var delivery = await Assert.That(state.InputReceived.Deliveries).HasSingleItem();
        state.Inbox = Request(state, delivery, ClusterRestoreMixedRetentionRf3Protocol.OriginalEffect);
        state.InputImage = (await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(new(state.Source, delivery.Id), token)))!;
        var failed = state.Inbox with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(ClusterRestoreMixedRetentionRf3Protocol.Documents, ClusterRestoreMixedRetentionRf3Protocol.OriginalEffect,
                ClusterRestoreMixedRetentionRf3Protocol.EventPayload, TargetInboxRf3Protocol.BadRevision)]
        };
        await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(callers, state, TargetInboxRf3Protocol.Tool, failed,
            () => callers.Sdk.CommitInboxAsync(failed, token), ErrorCode.RevisionConflict, token);
        await ClusterRestoreMixedRetentionRf3Literal.AbsentDocumentAsync(callers, state,
            ClusterRestoreMixedRetentionRf3Protocol.Documents, ClusterRestoreMixedRetentionRf3Protocol.OriginalEffect, token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Source.Partition, McpCallerTools.MessagesInspect,
            new InspectMessageRequest(state.Source, delivery.Id), state.InputImage,
            () => callers.Sdk.InspectAsync(new(state.Source, delivery.Id), token), token);
        state.InboxResult = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            TargetInboxRf3Protocol.Tool, state.Inbox, () => callers.Sdk.CommitInboxAsync(state.Inbox, token), token);
        await Assert.That(state.InboxResult.AlreadyProcessed).IsFalse();
        await ReceiptAsync(state.Inbox, state.InboxResult, state.TargetOwner);
        await SqlRf3Protocol.EqualAsync(state.InboxResult.Receipt.Token, state.InboxResult.OriginalEffectsToken);
        state.OutputImage = new(new(ClusterRestoreMixedRetentionRf3Protocol.OriginalEffect, MessageState.Ready,
            ClusterRestoreMixedRetentionRf3Protocol.NoAttempts, QueueLifecyclePublicProtocol.One, QueueLifecyclePublicProtocol.One, null, null),
            ClusterRestoreMixedRetentionRf3Protocol.EventPayload, ClusterRestoreMixedRetentionRf3Protocol.EventHeaders);
        state.DocumentImage = new(new(state.Partition, ClusterRestoreMixedRetentionRf3Protocol.Documents,
            ClusterRestoreMixedRetentionRf3Protocol.OriginalEffect), ClusterRestoreMixedRetentionRf3Protocol.FirstRevision,
            ClusterRestoreMixedRetentionRf3Protocol.EventPayload, false, []);
        var stream = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(new(Stream(state,
            ClusterRestoreMixedRetentionRf3Protocol.OriginalEffect)), token));
        state.InboxEvents = stream.Events;
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Partition, TargetInboxRf3Protocol.Tool,
            state.Inbox, state.InboxResult, () => callers.Sdk.CommitInboxAsync(state.Inbox, token), token);
        await ClusterRestoreMixedRetentionRf3Literal.InboxAsync(callers, state, token);
    }

    internal static StreamRef Stream(ClusterRestoreMixedRetentionRf3State state, string effect)
        => new(state.Partition, ClusterRestoreMixedRetentionRf3Protocol.Events, effect);

    private static CommitInboxRequest Request(ClusterRestoreMixedRetentionRf3State state, Delivery delivery, string effect)
        => new(Guid.NewGuid(), state.Target, state.Source, delivery.Id, delivery.DeliveryGeneration,
            ClusterRestoreMixedRetentionRf3Protocol.Handler, ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration,
            [new PutDocument(ClusterRestoreMixedRetentionRf3Protocol.Documents, effect, ClusterRestoreMixedRetentionRf3Protocol.EventPayload,
                ClusterRestoreMixedRetentionRf3Protocol.NoRevision),
             new AppendEvents(ClusterRestoreMixedRetentionRf3Protocol.Events, effect,
                [new(effect, ClusterRestoreMixedRetentionRf3Protocol.AcceptedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload,
                    ClusterRestoreMixedRetentionRf3Protocol.EventHeaders)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(ClusterRestoreMixedRetentionRf3Protocol.Output, effect,
                ClusterRestoreMixedRetentionRf3Protocol.EventPayload, ClusterRestoreMixedRetentionRf3Protocol.EventHeaders)]);

    internal static async Task ContinueAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await ReconcileOriginalInputAsync(callers, state, token).ConfigureAwait(false);
        var enqueue = new CommandRequest(Guid.NewGuid(), state.Source.Partition,
            [new EnqueueMessage(state.Source.Queue, ClusterRestoreMixedRetentionRf3Protocol.FreshInput,
                ClusterRestoreMixedRetentionRf3Protocol.EventPayload, ClusterRestoreMixedRetentionRf3Protocol.EventHeaders)]);
        state.FreshEnqueueReceipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Source.Partition,
            McpCallerTools.DocumentsCommit, enqueue, () => callers.Sdk.CommitAsync(enqueue, token), token);
        state.FreshEnqueue = enqueue;
        var request = new ReceiveRequest(Guid.NewGuid(), state.Source);
        var received = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Source.Partition,
            McpCallerTools.MessagesReceive, request, () => callers.Sdk.ReceiveAsync(request, token), token);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(ClusterRestoreMixedRetentionRf3Protocol.FreshInput);
        state.FreshInbox = Request(state, delivery, ClusterRestoreMixedRetentionRf3Protocol.FreshEffect);
        state.FreshInboxResult = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Partition,
            TargetInboxRf3Protocol.Tool, state.FreshInbox, () => callers.Sdk.CommitInboxAsync(state.FreshInbox, token), token);
        await Assert.That(state.FreshInboxResult.AlreadyProcessed).IsFalse();
        await ReceiptAsync(state.FreshInbox, state.FreshInboxResult, state.CurrentTargetOwner);
        await SqlRf3Protocol.EqualAsync(state.FreshInboxResult.Receipt.Token, state.FreshInboxResult.OriginalEffectsToken);
        state.FreshAck = new(Guid.NewGuid(), state.Source, delivery.Token, DeliveryAction.Ack);
        state.FreshAckReceipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Source.Partition,
            McpCallerTools.MessagesComplete, state.FreshAck, () => callers.Sdk.CompleteAsync(state.FreshAck, token), token);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(state.FreshAck.CommandId,
            new(state.CurrentSourceOwner.Incarnation, state.Source.Partition.AtomicPartitionId,
                state.FreshAckReceipt.Token.Position, state.CurrentSourceOwner.PlacementEpoch),
            [new(ClusterRestoreMixedRetentionRf3Protocol.AckKind, state.Source.Queue, delivery.Id, QueueLifecyclePublicProtocol.Three)],
            DurabilityProfile.QuorumProcessDurable), state.FreshAckReceipt);
        state.FreshInputImage = new(new(delivery.Id, MessageState.Acked, QueueLifecyclePublicProtocol.One,
            QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.Two, null, null,
            LeaseVersion: delivery.LeaseVersion), null, null);
        state.FreshInboxEvents = (await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(
            new(Stream(state, ClusterRestoreMixedRetentionRf3Protocol.FreshEffect)), token))).Events;
    }
    private static async Task ReceiptAsync(CommitInboxRequest request, CommitInboxResult actual, PhysicalShardRecord owner)
    {
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(ClusterRestoreMixedRetentionRf3Protocol.NoRevision);
        var effect = ((PutDocument)request.Effects[ClusterRestoreMixedRetentionRf3Protocol.FirstIndex]).Id;
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(request.CommandId,
            new(owner.Incarnation, request.Target.Partition.AtomicPartitionId, actual.Receipt.Token.Position, owner.PlacementEpoch),
            [new(ClusterRestoreMixedRetentionRf3Protocol.PutKind, ClusterRestoreMixedRetentionRf3Protocol.Documents, effect, ClusterRestoreMixedRetentionRf3Protocol.FirstRevision),
             new(ClusterRestoreMixedRetentionRf3Protocol.AppendKind, ClusterRestoreMixedRetentionRf3Protocol.Events, effect, ClusterRestoreMixedRetentionRf3Protocol.FirstRevision),
             new(ClusterRestoreMixedRetentionRf3Protocol.EnqueueKind, ClusterRestoreMixedRetentionRf3Protocol.Output, effect, ClusterRestoreMixedRetentionRf3Protocol.FirstRevision)],
            DurabilityProfile.QuorumProcessDurable), actual.Receipt);
    }

    private static async Task ReconcileOriginalInputAsync(RequestCqrsRf3Callers callers,
        ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var duplicate = state.Inbox with { CommandId = Guid.NewGuid() };
        var duplicated = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Partition,
            TargetInboxRf3Protocol.Tool, duplicate, () => callers.Sdk.CommitInboxAsync(duplicate, token), token);
        await SqlRf3Protocol.EqualAsync(state.InboxResult with { AlreadyProcessed = true }, duplicated);
        await ClusterRestoreMixedRetentionRf3Literal.InboxAsync(callers, state, token);
        var current = (await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(
            new(state.Source, ClusterRestoreMixedRetentionRf3Protocol.OriginalInput), token)))!;
        var cancel = new CommandRequest(Guid.NewGuid(), state.Source.Partition,
            [new CancelQueueMessage(state.Source.Queue, current.Metadata.Id, current.Metadata.StateVersion, current.Metadata.DeliveryGeneration)]);
        state.SourceCancelReceipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Source.Partition,
            McpCallerTools.DocumentsCommit, cancel, () => callers.Sdk.CommitAsync(cancel, token), token);
        state.SourceCancel = cancel;
        state.FinalOriginalInput = new(current.Metadata with
        {
            State = MessageState.Cancelled,
            StateVersion = current.Metadata.StateVersion + QueueLifecyclePublicProtocol.One,
            DeliveryGeneration = current.Metadata.DeliveryGeneration + QueueLifecyclePublicProtocol.One,
            LeaseVersion = current.Metadata.LeaseVersion + QueueLifecyclePublicProtocol.One,
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null,
            ParkedSequence = ClusterRestoreMixedRetentionRf3Protocol.NoRevision,
            ActiveOrderSequence = ClusterRestoreMixedRetentionRf3Protocol.NoRevision
        }, null, null);
    }

}

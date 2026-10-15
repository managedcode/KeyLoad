using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineCapacityHealthy
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, string directory,
        QueueDeadlineCapacityState state, MessageMetadata original, long sequence, Delivery? active,
        DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.State == MessageState.Ready)
        {
            active = await ReceiveAsync(database, store, state, original, time, token);
            original = QueueDeadlineCapacityClaims.Leased(original, time);
        }
        if (active is not null)
        {
            original = await AckAsync(database, store, state, original, active, time, token);
        }
        await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, original)], sequence,
            QueueDeadlineCapacityOperations.ParkedSequence(state, original));
        var input = new EnqueueMessage(Queue, HealthyMessage, Json, Json);
        var request = new CommandRequest(Guid.NewGuid(), state.Lane.Partition, [input]);
        var operation = QueueDeadlinePoisonPrefixClaims.Operation(database, OperationKind.Batch, request.CommandId,
            request, Root, time);
        var expected = new CommitReceipt(request.CommandId, QueueDeadlinePoisonPrefixClaims.NextToken(database, store, state.Lane),
            [new(EnqueueReceipt, Queue, HealthyMessage, InitialStateVersion)], DurabilityProfile.ProcessDurable);
        var result = database.Apply(operation);
        await QueueDeadlineCapacityOperations.ReceiptAsync(result, expected);
        state.Outcomes.Add((operation, result));
        var healthy = new MessageMetadata(HealthyMessage, MessageState.Ready, NoAttempts, InitialStateVersion, sequence + ReadySequenceStep, null, null);
        sequence += ReadySequenceStep;
        await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, original), (input, healthy)], sequence,
            QueueDeadlineCapacityOperations.ParkedSequence(state, original));
        var claim = await ReceiveAsync(database, store, state, healthy, time, token);
        healthy = QueueDeadlineCapacityClaims.Leased(healthy, time);
        await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, original), (input, healthy)], sequence,
            QueueDeadlineCapacityOperations.ParkedSequence(state, original));
        healthy = await AckAsync(database, store, state, healthy, claim, time, token);
        await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, original), (input, healthy)], sequence,
            QueueDeadlineCapacityOperations.ParkedSequence(state, original));
        await QueueDeadlineCapacityCold.RunAsync(database, store, directory, state,
            [(state.Input, original), (input, healthy)], sequence, token);
    }

    private static async Task<Delivery> ReceiveAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadlineCapacityState state, MessageMetadata metadata, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var expected = QueueDeadlineCapacityClaims.Expected(database, state.Lane, metadata, request.RequestId,
            QueueDeadlinePoisonPrefixClaims.NextToken(database, store, state.Lane), time);
        var operation = QueueDeadlinePoisonPrefixClaims.Operation(database, OperationKind.Receive, request.RequestId, request, Root, time);
        var result = database.Apply(operation);
        await QueueDeadlineCapacityClaims.RequireAsync(database, result.Get<ReceiveResult>(), expected, state.Lane);
        state.Outcomes.Add((operation, result));
        return result.Get<ReceiveResult>().Deliveries.Single();
    }

    private static async Task<MessageMetadata> AckAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadlineCapacityState state, MessageMetadata metadata, Delivery claim, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, claim.Token, DeliveryAction.Ack);
        var expected = new CommitReceipt(request.CommandId, QueueDeadlinePoisonPrefixClaims.NextToken(database, store, state.Lane),
            [new(AckReceipt, Queue, metadata.Id, metadata.StateVersion + StateVersionStep)], DurabilityProfile.ProcessDurable);
        var operation = QueueDeadlinePoisonPrefixClaims.Operation(database, OperationKind.Delivery, request.CommandId, request, Root, time);
        var result = database.Apply(operation);
        await QueueDeadlineCapacityOperations.ReceiptAsync(result, expected);
        state.Outcomes.Add((operation, result));
        return metadata with
        {
            State = MessageState.Acked,
            StateVersion = metadata.StateVersion + StateVersionStep,
            LeaseOwner = null,
            LeaseUntil = null
        };
    }
}

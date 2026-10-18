using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineCapacityContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, string directory,
        QueueDeadlineCapacityState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var positive = state.Fault is QueueDeadlineCapacityFault.FullDeadLetterMaxPark or QueueDeadlineCapacityFault.ExpiryBeforeDeadLetterMaxPark;
        var operation = positive ? state.Attempt : QueueDeadlineCapacityOperations.Due(database, state.Lane,
            state.Original, state.Due, state.Kind, state.Sweep);
        var commit = QueueDeadlinePoisonPrefixClaims.NextToken(database, store, state.Lane);
        var expected = await QueueDeadlineCapacityDecision.ExpectedAsync(state, operation);
        var sequence = expected.State == MessageState.Ready ? state.Counters.NextReadySequence + ReadySequenceStep : state.Counters.NextReadySequence;
        expected = expected with
        {
            ReadySequence = expected.State == MessageState.Ready ? sequence : expected.ReadySequence
        };
        ReceiveResult? claimed = null;
        if (state.Sweep)
        {
            claimed = QueueDeadlineCapacityClaims.Expected(database, state.Lane, expected, operation.Id, commit, state.Due);
            if (expected.State == MessageState.Ready)
            {
                expected = QueueDeadlineCapacityClaims.Leased(expected, state.Due);
            }
        }
        var actual = database.Apply(operation);
        if (claimed is null)
        {
            await QueueDeadlineCapacityOperations.ReceiptAsync(actual, new(operation.Id, commit, [new(AdvanceReceipt, Queue, Message, expected.StateVersion)], DurabilityProfile.ProcessDurable, 1));
        }
        else
        {
            await QueueDeadlineCapacityClaims.RequireAsync(database, actual.Get<ReceiveResult>(), claimed, state.Lane);
        }
        state.Outcomes.Add((operation, actual));
        await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, expected)], sequence,
            QueueDeadlineCapacityOperations.ParkedSequence(state, expected));
        var time = state.Due;
        if (expected.State == MessageState.Scheduled)
        {
            time = expected.NotBefore!.Value;
            expected = await PromoteAsync(database, store, state, expected, sequence);
            sequence = expected.ReadySequence;
            await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, expected)], sequence,
            QueueDeadlineCapacityOperations.ParkedSequence(state, expected));
        }
        await QueueDeadlineCapacityHealthy.RunAsync(database, store, directory, state, expected, sequence,
            claimed?.Deliveries.FirstOrDefault(), time, token);
    }

    private static async Task<MessageMetadata> PromoteAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadlineCapacityState state, MessageMetadata metadata, long sequence)
    {
        var operation = QueueDeadlineCapacityOperations.Due(database, state.Lane, metadata, metadata.NotBefore!.Value,
            QueueDeadlineKind.PromoteScheduled, false);
        var commit = QueueDeadlinePoisonPrefixClaims.NextToken(database, store, state.Lane);
        var expected = metadata with
        {
            State = MessageState.Ready,
            NotBefore = null,
            StateVersion = metadata.StateVersion + StateVersionStep,
            ReadySequence = sequence + ReadySequenceStep
        };
        var result = database.Apply(operation);
        await QueueDeadlineCapacityOperations.ReceiptAsync(result, new(operation.Id, commit, [new(AdvanceReceipt, Queue, Message, expected.StateVersion)], DurabilityProfile.ProcessDurable, 1));
        state.Outcomes.Add((operation, result));
        return expected;
    }
}

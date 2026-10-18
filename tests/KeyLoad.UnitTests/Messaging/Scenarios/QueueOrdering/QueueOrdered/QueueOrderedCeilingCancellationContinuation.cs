using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedCeilingCancellationContinuation
{
    private const int OriginalTerminalVersion = 6;
    private const int OriginalGeneration = 1;
    private const int CancelledVersion = 7;
    private const int CancelledGeneration = 2;
    private const int CancelledLeaseVersion = 3;
    private const int OriginalAttempts = 2;
    private const int SourceReadyPosition = 4;
    private const int HealthyReadyPosition = 5;
    private const int HealthyOrderPosition = 4;
    private const int InitialLeaseVersion = 1;
    private const int AckedVersion = 3;
    private const int InitialAttempt = 1;
    private const int EmptyUsage = 0;
    private const int ParkedPosition = 1;
    private const string Exhausted = "AttemptsExhausted";
    private const string Counters = "queue-counters";

    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Batch(database, new CancelQueueMessage(state.Lane.Queue, QueueOrderedRetryProtocol.First,
            OriginalTerminalVersion, OriginalGeneration)).Get<CommitReceipt>();
        var image = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var stale = QueueOrderedRetryOperations.Complete(database, state, state.FirstLease!, DeliveryAction.Ack);
        await Assert.That(Errors.Code(stale.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(stale.Value).IsNull();
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, image);
        QueueOrderedRetryOperations.Complete(database, state, state.SecondLease!, DeliveryAction.Ack).Get<CommitReceipt>();
    }

    internal static async Task TerminalAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        await QueueOrderedRetryAssertions.MessageAsync(database, state,
            new(QueueOrderedRetryProtocol.First, MessageState.Cancelled, OriginalAttempts, CancelledVersion,
                SourceReadyPosition, null, null, LeaseVersion: CancelledLeaseVersion, DeliveryGeneration: CancelledGeneration,
                SafeFailureCode: Exhausted, EnqueueSequence: QueueOrderedRetryProtocol.One), stored: false);
        await AckedAsync(database, state, QueueOrderedRetryProtocol.Second, QueueOrderedRetryProtocol.Two, QueueOrderedRetryProtocol.Two);
        await AckedAsync(database, state, QueueOrderedRetryProtocol.Other, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.Three);
        await AckedAsync(database, state, QueueOrderedRetryProtocol.Healthy, HealthyReadyPosition, HealthyOrderPosition);
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(Counters, state.Partition, state.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(EmptyUsage, EmptyUsage, EmptyUsage, EmptyUsage,
            HealthyReadyPosition, NextParkedSequence: ParkedPosition, NextOrderSequence: HealthyOrderPosition));
        await QueueLifecycleCeilingTerminalIndices.EmptyAsync((ZoneTreeStore)database.Store, state.Lane);
    }

    private static Task AckedAsync(DatabaseEngine database, QueueOrderedRetryState state, string id, long ready, long order)
        => QueueOrderedRetryAssertions.MessageAsync(database, state,
            new(id, MessageState.Acked, InitialAttempt, AckedVersion, ready, null, null,
                LeaseVersion: InitialLeaseVersion, EnqueueSequence: order), stored: false);
}

using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueRedriveUnusedSequenceAssertions
{
    private const int Empty = 0;
    private const int ScheduledVersion = 7;
    private const int CancelledVersion = 8;
    private const int CancelledGeneration = 3;
    private const int CancelledLeaseVersion = 4;
    private const int ReadyAfterScheduledControl = 5;
    private const int OrderAfterScheduledControl = 5;
    private const int ReadyAfterBlockControl = 5;
    private const int AckedVersion = 3;
    private const string ScheduledSpace = "scheduled";

    internal static async Task ScheduledAsync(DatabaseEngine database, QueueOrderedRetryState state, DateTimeOffset available)
    {
        await QueueOrderedRetryAssertions.MessageAsync(database, state,
            new(QueueOrderedRetryProtocol.First, MessageState.Scheduled, Empty, ScheduledVersion, Empty, available, null,
                LeaseVersion: QueueOrderedRetryProtocol.Three, DeliveryGeneration: QueueOrderedRetryProtocol.Two,
                EnqueueSequence: QueueOrderedRetryProtocol.One, ActiveOrderSequence: QueueOrderedRetryProtocol.Four), stored: true);
        var reference = database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition(ScheduledSpace,
            state.Partition, state.Lane.Queue, available, QueueOrderedRetryProtocol.First)));
        await Assert.That(reference).IsEqualTo(QueueOrderedRetryProtocol.First);
        await Assert.That(QueueLifecycleControlCold.Read(database, state.Lane).NextReadySequence).IsEqualTo(long.MaxValue);
    }

    internal static async Task ExhaustedCountersAsync(DatabaseEngine database, QueueOrderedRetryState state, bool block)
    {
        await Assert.That(QueueLifecycleControlCold.Read(database, state.Lane)).IsEqualTo(new QueueCounters(
            Empty, Empty, Empty, Empty, block ? ReadyAfterBlockControl : long.MaxValue,
            NextParkedSequence: QueueOrderedRetryProtocol.One,
            NextOrderSequence: block ? long.MaxValue : QueueOrderedRetryProtocol.Four));
        await QueueLifecycleCeilingTerminalIndices.EmptyAsync((ZoneTreeStore)database.Store, state.Lane,
            QueueOrderedRetryProtocol.Three);
    }

    internal static async Task TerminalAsync(DatabaseEngine database, QueueOrderedRetryState state, bool block)
    {
        if (block)
        {
            await QueueOrderedRetryAssertions.TerminalAsync(database, state, healthy: true);
        }
        else
        {
            await QueueOrderedRetryAssertions.MessageAsync(database, state,
                new(QueueOrderedRetryProtocol.First, MessageState.Cancelled, Empty, CancelledVersion, Empty, null, null,
                    LeaseVersion: CancelledLeaseVersion, DeliveryGeneration: CancelledGeneration,
                    EnqueueSequence: QueueOrderedRetryProtocol.One), stored: false);
            await AckedAsync(database, state, QueueOrderedRetryProtocol.Second, QueueOrderedRetryProtocol.Two, QueueOrderedRetryProtocol.Two);
            await AckedAsync(database, state, QueueOrderedRetryProtocol.Other, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.Three);
            await AckedAsync(database, state, QueueOrderedRetryProtocol.Healthy, ReadyAfterScheduledControl, OrderAfterScheduledControl);
            await Assert.That(QueueLifecycleControlCold.Read(database, state.Lane)).IsEqualTo(new QueueCounters(
                Empty, Empty, Empty, Empty, ReadyAfterScheduledControl, NextParkedSequence: QueueOrderedRetryProtocol.One,
                NextOrderSequence: OrderAfterScheduledControl));
        }
        await QueueLifecycleCeilingTerminalIndices.EmptyAsync((ZoneTreeStore)database.Store, state.Lane);
    }

    private static Task AckedAsync(DatabaseEngine database, QueueOrderedRetryState state, string id, long ready, long order)
        => QueueOrderedRetryAssertions.MessageAsync(database, state, new(id, MessageState.Acked, QueueOrderedRetryProtocol.One,
            AckedVersion, ready, null, null, LeaseVersion: QueueOrderedRetryProtocol.One, EnqueueSequence: order), stored: false);
}

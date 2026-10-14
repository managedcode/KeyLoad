using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueuePendingAuthorityImage
{
    internal static async Task RequireAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        QueuePendingAuthorityPhase phase)
    {
        var original = QueuePendingAuthorityHistory.Capture(store);
        await MessageAsync(owner, store, state, new(QueueLifecycleTestProtocol.Parked, MessageState.Cancelled, 1, 4, 1,
            null, null, LeaseVersion: 2, DeliveryGeneration: 2, SafeFailureCode: QueuePendingAuthorityProtocol.Exhausted));
        await MessageAsync(owner, store, state, new(QueuePendingAuthorityProtocol.History, MessageState.Acked, 1, 3, 3,
            null, null, LeaseVersion: 1));
        var pending = phase switch
        {
            QueuePendingAuthorityPhase.Pending => new MessageMetadata(QueueLifecycleTestProtocol.Pending,
                MessageState.PendingDeadLetter, 1, 3, 2, null, null, LeaseVersion: 1,
                SafeFailureCode: QueuePendingAuthorityProtocol.Exhausted),
            QueuePendingAuthorityPhase.Parked => new MessageMetadata(QueueLifecycleTestProtocol.Pending,
                MessageState.DeadLettered, 1, 4, 2, null, null, LeaseVersion: 1,
                SafeFailureCode: QueuePendingAuthorityProtocol.Exhausted, ParkedSequence: 2),
            _ => new MessageMetadata(QueueLifecycleTestProtocol.Pending, MessageState.Acked, 1, 7, 4, null, null,
                LeaseVersion: 3, DeliveryGeneration: 2)
        };
        await MessageAsync(owner, store, state, pending);
        if (phase == QueuePendingAuthorityPhase.Terminal)
        {
            await MessageAsync(owner, store, state, new(QueuePendingAuthorityProtocol.Healthy, MessageState.Acked, 1, 3, 5,
                null, null, LeaseVersion: 1));
        }
        var bytes = NativeSerialization.Serialize(Body(QueueLifecycleTestProtocol.Pending)).LongLength;
        var expected = phase switch
        {
            QueuePendingAuthorityPhase.Pending => new QueueCounters(1, bytes, 0, 0, 3, 0, 0, 1),
            QueuePendingAuthorityPhase.Parked => new QueueCounters(1, bytes, 0, 0, 3, 1, bytes, 2),
            _ => new QueueCounters(0, 0, 0, 0, 5, 0, 0, 2)
        };
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(QueuePendingAuthorityOperations.Key(
            state.Flow, QueueLifecycleTestProtocol.CounterSpace)))).IsEqualTo(expected);
        await IndicesAsync(store, state, phase);
        await QueuePendingAuthorityEffects.RequireAsync(owner, store, state);
        await QueuePendingAuthorityHistory.SameAsync(store, original);
    }

    private static async Task MessageAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        MessageMetadata metadata)
    {
        var retained = metadata.State is MessageState.PendingDeadLetter or MessageState.DeadLettered;
        await Assert.That(owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Flow.Lane, metadata.Id))
            .IsEqualTo(new MessageInspection(metadata, retained ? QueueLifecycleTestProtocol.Payload : null,
                retained ? QueueLifecycleTestProtocol.Headers : null));
        await Assert.That(store.Read(view => view.GetRecord<MessageMetadata>(QueuePendingAuthorityOperations.Key(
            state.Flow, QueueLifecycleTestProtocol.MetadataSpace, metadata.Id)))).IsEqualTo(metadata);
        var body = store.Read(view => view.GetRecord<MessageBody>(QueuePendingAuthorityOperations.Key(
            state.Flow, QueueLifecycleTestProtocol.BodySpace, metadata.Id)));
        await Assert.That(body).IsEqualTo(retained ? Body(metadata.Id) : null);
    }

    private static async Task IndicesAsync(ZoneTreeStore store, QueuePendingAuthorityState state, QueuePendingAuthorityPhase phase)
    {
        foreach (var family in new[] { QueueLifecycleTestProtocol.ReadySpace, QueueLifecycleTestProtocol.ScheduledSpace,
            QueueLifecycleTestProtocol.LeaseSpace, QueuePendingAuthorityProtocol.Order })
        {
            await EmptyAsync(store, state, family);
        }
        if (phase == QueuePendingAuthorityPhase.Pending)
        {
            await Assert.That(store.Read(view => view.GetRecord<QueuePendingDeadLetterReference>(state.MarkerKey)))
                .IsEqualTo(new QueuePendingDeadLetterReference(QueueLifecycleTestProtocol.Pending, 1, QueuePendingAuthorityProtocol.Exhausted));
            await CountAsync(store, state, QueueLifecycleTestProtocol.PendingSpace, 1);
        }
        else
        {
            await EmptyAsync(store, state, QueueLifecycleTestProtocol.PendingSpace);
        }
        if (phase == QueuePendingAuthorityPhase.Parked)
        {
            await Assert.That(store.Read(view => view.GetRecord<string>(QueuePendingAuthorityOperations.Key(state.Flow,
                QueueLifecycleTestProtocol.DeadLetterSpace, QueueLifecycleTestProtocol.Pending)))).IsEqualTo(QueueLifecycleTestProtocol.Pending);
            await Assert.That(store.Read(view => view.GetRecord<QueueDeadLetterReference>(QueuePendingAuthorityOperations.Key(
                state.Flow, QueueLifecycleTestProtocol.ParkedSpace, 2L, QueueLifecycleTestProtocol.Pending))))
                .IsEqualTo(new QueueDeadLetterReference(QueueLifecycleTestProtocol.Pending, 2, 1, QueuePendingAuthorityProtocol.Exhausted));
        }
        await CountAsync(store, state, QueueLifecycleTestProtocol.DeadLetterSpace, phase == QueuePendingAuthorityPhase.Parked ? 1 : 0);
        await CountAsync(store, state, QueueLifecycleTestProtocol.ParkedSpace, phase == QueuePendingAuthorityPhase.Parked ? 1 : 0);
        await CountAsync(store, state, QueueLifecycleTestProtocol.BodySpace, phase == QueuePendingAuthorityPhase.Terminal ? 0 : 1);
        await CountAsync(store, state, QueueLifecycleTestProtocol.MetadataSpace, phase == QueuePendingAuthorityPhase.Terminal ? 4 : 3);
    }

    private static Task EmptyAsync(ZoneTreeStore store, QueuePendingAuthorityState state, string family)
        => CountAsync(store, state, family, 0);

    private static async Task CountAsync(ZoneTreeStore store, QueuePendingAuthorityState state, string family, int count)
    {
        var page = store.Read(view => view.Scan(QueuePendingAuthorityOperations.Key(state.Flow, family), QueueLifecycleTestProtocol.ImageRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(count);
    }

    private static MessageBody Body(string id)
    {
        var literal = QueueLifecycleHealthy.Enqueue(id);
        return new(id, literal.PayloadJson, literal.HeadersJson, literal.OrderingKey, JsonData.Fingerprint(literal));
    }
}

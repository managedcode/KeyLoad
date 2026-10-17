using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleAccountingAssertions
{
    internal static long BodyBytes(string id)
    {
        var literal = QueueLifecycleHealthy.Enqueue(id);
        return NativeSerialization.Serialize(new MessageBody(id, literal.PayloadJson, literal.HeadersJson,
            literal.OrderingKey, JsonData.Fingerprint(literal))).LongLength;
    }

    internal static async Task InitialAsync(ZoneTreeStore store, QueueLifecycleTestState state)
        => await CountersAsync(store, state, new(QueueLifecycleTestProtocol.Three,
            BodyBytes(QueueLifecycleTestProtocol.Parked) + BodyBytes(QueueLifecycleTestProtocol.Pending) + BodyBytes(QueueLifecycleTestProtocol.Held),
            QueueLifecycleTestProtocol.One, BodyBytes(QueueLifecycleTestProtocol.Held), QueueLifecycleTestProtocol.Three,
            QueueLifecycleTestProtocol.One, BodyBytes(QueueLifecycleTestProtocol.Parked), QueueLifecycleTestProtocol.One));

    internal static async Task TerminalAsync(ZoneTreeStore store, QueueLifecycleTestState state, bool healthy)
    {
        await CountersAsync(store, state, new(QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.None,
            QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.None,
            healthy ? QueueLifecycleTestProtocol.Five : QueueLifecycleTestProtocol.Four,
            QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.Two));
        foreach (var family in new[] { QueueLifecycleTestProtocol.ReadySpace, QueueLifecycleTestProtocol.ScheduledSpace,
            QueueLifecycleTestProtocol.LeaseSpace, QueueLifecycleTestProtocol.BodySpace, QueueLifecycleTestProtocol.DeadLetterSpace,
            QueueLifecycleTestProtocol.PendingSpace, QueueLifecycleTestProtocol.ParkedSpace })
        {
            var page = store.Read(view => view.Scan(KeySpace.Partition(family, state.Partition, state.Lane.Queue), QueueLifecycleTestProtocol.One));
            await Assert.That(page.Records).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
        }
    }

    private static async Task CountersAsync(ZoneTreeStore store, QueueLifecycleTestState state, QueueCounters expected)
        => await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueLifecycleTestProtocol.CounterSpace, state.Partition, state.Lane.Queue)))).IsEqualTo(expected);
}
